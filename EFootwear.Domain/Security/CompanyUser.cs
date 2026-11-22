using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// A login account inside one company. Table: sec.users.
/// PasswordHash is for the data layer only. Never put it in an API response or a listing.
/// </summary>
public sealed class CompanyUser : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";         // sign-in name, unique across all companies
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public bool AllStores { get; set; }             // true = sees every store of the company (company owner)
    public string Status { get; set; } = UserStatusCodes.Active;
    public bool MustChangePassword { get; set; } = true;
    public DateTimeOffset? LockedUntil { get; set; }  // set by the sign-in function after too many wrong passwords
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
