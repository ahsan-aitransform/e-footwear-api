namespace EFootwear.BLL.Abstractions;

/// <summary>
/// Who is making this request. Filled by the authentication layer from a validated session,
/// never from the request body or URL.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid CompanyId { get; }
    Guid UserId { get; }
    string RoleCode { get; }
    bool AllStores { get; }
    IReadOnlyList<Guid> StoreIds { get; }

    /// <summary>True when the user may act on this store (owner, or store is assigned).</summary>
    bool CanAccessStore(Guid storeId);
}
