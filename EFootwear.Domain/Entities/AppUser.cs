namespace EFootwear.Domain.Entities;

/// <summary>
/// A login account inside one company. Table: sec.users.
/// PasswordHash is for the DAL only. Never put it in an API response or a listing.
/// </summary>
public sealed class AppUser
{
    public long CompanyId { get; set; }
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";         // sign-in name, unique across all companies
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public bool AllStores { get; set; }             // true = sees every store of the company (company owner)
    public string Status { get; set; } = "ACTIVE";  // ACTIVE, LOCKED, DISABLED
    public bool MustChangePassword { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}
