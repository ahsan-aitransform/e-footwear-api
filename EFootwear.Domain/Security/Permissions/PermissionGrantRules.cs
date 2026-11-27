using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// The rules for changing the permissions of a user or a role template (Part 6). Pure code, unit-tested without a database.
///
///   * Every code must exist in the catalog (nothing outside the registered screens can be granted).
///   * A person can only ADD permissions they hold themselves, so nobody can raise their own or somebody else's rights above
///     their own. The company owner holds the whole catalog. Removing a permission needs no such condition.
///   * A role template is only a starting point: applying one copies its codes, and an individual override is always possible.
/// </summary>
public static class PermissionGrantRules
{
    public static void EnsureCodesAreKnown(IEnumerable<string> permissionCodes)
    {
        var unknownCodes = permissionCodes
            .Where(code => !PermissionCatalog.IsKnownPermissionCode(code))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();
        if (unknownCodes.Count > 0)
            throw new DomainException(ErrorKind.Validation,
                $"These permission codes do not exist: {string.Join(", ", unknownCodes)}.");
    }

    /// <summary>For a user other than the owner, and for a role template.</summary>
    public static void EnsureNotOwnerOnly(IEnumerable<string> permissionCodes)
    {
        var ownerOnly = permissionCodes.Where(OwnerOnlyPermissions.Contains).Take(5).ToList();
        if (ownerOnly.Count > 0)
            throw new DomainException(ErrorKind.Validation,
                $"Only the company owner can manage prices and discounts. These cannot be granted: {string.Join(", ", ownerOnly)}.");
    }

    public static void EnsureCanGrant(IReadOnlySet<string> codesHeldByCaller, IReadOnlyCollection<string> codesBeingAdded)
    {
        var notHeld = codesBeingAdded.Where(code => !codesHeldByCaller.Contains(code)).Take(5).ToList();
        if (notHeld.Count > 0)
            throw new DomainException(ErrorKind.Forbidden,
                $"You can only grant permissions you hold yourself. Not held: {string.Join(", ", notHeld)}.",
                ErrorCodes.PermissionDenied);
    }

    public static PermissionChange Compare(IReadOnlySet<string> currentCodes, IEnumerable<string> requestedCodes)
    {
        var requested = requestedCodes.ToHashSet(StringComparer.Ordinal);
        var added = requested.Where(code => !currentCodes.Contains(code)).Order(StringComparer.Ordinal).ToList();
        var removed = currentCodes.Where(code => !requested.Contains(code)).Order(StringComparer.Ordinal).ToList();
        return new PermissionChange(added, removed);
    }
}
