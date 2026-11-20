namespace EFootwear.Domain.Entities;

/// <summary>Which store a user may work in. Table: sec.user_stores. Not used when AppUser.AllStores is true.</summary>
public sealed class UserStoreAccess
{
    public long CompanyId { get; set; }
    public long UserId { get; set; }
    public long StoreId { get; set; }
}
