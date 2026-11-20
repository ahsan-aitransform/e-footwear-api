using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>Who is asking to create a user. Built by the BLL from the validated session, never from the request.</summary>
public sealed record Caller(bool IsPlatformAdmin, string? RoleCode, bool AllStores, IReadOnlySet<long> StoreIds);

/// <summary>
/// The user-creation rules, as pure code so they can be unit-tested without a database.
///
///   Platform administrator  -> creates COMPANY_OWNER (no stores) and STORE_MANAGER (exactly one store)
///   Company owner           -> creates STORE_MANAGER, CASHIER_SALESMAN, ACCOUNTANT in any store of the company
///   Store manager           -> creates CASHIER_SALESMAN, ACCOUNTANT only, in its own stores
///   Everyone else           -> creates nobody
///
/// The database checks the company and the store ids (they must belong to the company and not be head office).
/// </summary>
public static partial class UserCreationRules
{
    public const int MinPasswordLength = 8;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>Roles this caller may create.</summary>
    public static IReadOnlyList<string> CreatableRoles(Caller caller)
    {
        if (caller.IsPlatformAdmin)
            return [RoleCodes.CompanyOwner, RoleCodes.StoreManager];

        return caller.RoleCode switch
        {
            RoleCodes.CompanyOwner => [RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant],
            RoleCodes.StoreManager => [RoleCodes.CashierSalesman, RoleCodes.Accountant],
            _ => [],
        };
    }

    /// <summary>Throws when the caller may not create a user with this role and these stores.</summary>
    public static void EnsureCanCreate(Caller caller, string targetRole, IReadOnlyCollection<long> storeIds)
    {
        if (!CreatableRoles(caller).Contains(targetRole))
            throw new DomainException(ErrorKind.Forbidden, $"You are not allowed to create a user with role {targetRole}.");

        if (targetRole == RoleCodes.CompanyOwner)
        {
            if (storeIds.Count > 0)
                throw new DomainException(ErrorKind.Validation, "A company owner works across all stores, so do not choose stores.");
            return;
        }

        if (storeIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Choose the store for this user.");

        if (storeIds.Any(s => s <= 0))
            throw new DomainException(ErrorKind.Validation, "Choose a store. Head office cannot hold store staff.");

        if (targetRole == RoleCodes.StoreManager && storeIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "A store manager heads exactly one store.");

        // A store manager may only add staff to the stores it is assigned to.
        if (!caller.IsPlatformAdmin && !caller.AllStores && storeIds.Any(s => !caller.StoreIds.Contains(s)))
            throw new DomainException(ErrorKind.Forbidden, "You can only add users to your own stores.");
    }

    public static void EnsureValidAccount(string email, string fullName, string password)
    {
        if ((email ?? "").Length > 254 || !EmailPattern().IsMatch(email ?? ""))
            throw new DomainException(ErrorKind.Validation, "Enter a valid email address.");

        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException(ErrorKind.Validation, "Full name is required.");

        if ((password ?? "").Length < MinPasswordLength)
            throw new DomainException(ErrorKind.Validation,
                $"Temporary password needs at least {MinPasswordLength} characters.");
    }
}
