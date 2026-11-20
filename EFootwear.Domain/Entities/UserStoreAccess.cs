namespace EFootwear.Domain.Entities;

/// <summary>Which store a user may work in. Table: sec.user_stores. Not used when AppUser.AllStores is true.</summary>
public sealed class UserStoreAccess
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public Guid StoreId { get; set; }
}
