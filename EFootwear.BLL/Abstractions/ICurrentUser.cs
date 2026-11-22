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
    bool HasAccessToAllStores { get; }
    IReadOnlyList<Guid> AssignedStoreIds { get; }

    /// <summary>Every permission code the user holds (the company owner holds the whole catalog).</summary>
    IReadOnlySet<string> GrantedPermissionCodes { get; }

    /// <summary>True when the user holds this permission code.</summary>
    bool HasPermission(string permissionCode);

    /// <summary>True when the user may act on this store (owner, or store is assigned).</summary>
    bool CanAccessStore(Guid storeId);
}
