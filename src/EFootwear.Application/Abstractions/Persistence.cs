namespace EFootwear.Application.Abstractions;

/// <summary>
/// One database transaction. Tenant work runs as the RLS-restricted role <c>pos_app</c> with the caller's company, user and
/// store scope set transaction-locally, so a pooled connection can never leak one tenant's scope to another.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IAuthRepository Auth { get; }
    IUserRepository Users { get; }
    IStoreRepository Stores { get; }
    IMasterRepository Masters { get; }
    IPartyRepository Parties { get; }
    ICatalogRepository Catalog { get; }
    IStockRepository Stock { get; }
    IPromoRepository Promos { get; }
    ISalesRepository Sales { get; }
    IPurchaseRepository Purchases { get; }
    IStockMoves StockMoves { get; }
    ITransferRepository Transfers { get; }
    IInventoryDocsRepository Inventory { get; }
    IFinanceRepository Finance { get; }

    /// <summary>Commits. Deferred triggers (balanced journals, stock rules) fire here, so a failure may surface from this call.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

public interface IUnitOfWorkFactory
{
    /// <summary>Full tenant scope: company, user, stores. Used for every authenticated request.</summary>
    Task<IUnitOfWork> BeginAsync(UserContext user, CancellationToken ct = default);

    /// <summary>Company scope only (login, session validation). No user or store scope.</summary>
    Task<IUnitOfWork> BeginForCompanyAsync(long companyId, CancellationToken ct = default);

    /// <summary>Company scope with every store (head office included). Internal jobs only, e.g. rebuilding effective prices.</summary>
    Task<IUnitOfWork> BeginSystemAsync(long companyId, CancellationToken ct = default) => BeginSystemAsync(companyId, null, ct);

    /// <summary>As above, acting for <paramref name="userId"/> (so database functions that read the session user, or check that user's permission, still see them).</summary>
    Task<IUnitOfWork> BeginSystemAsync(long companyId, long? userId, CancellationToken ct = default);

    /// <summary>No scope at all. Only for looking a company up by name or id before login.</summary>
    Task<IUnitOfWork> BeginAnonymousAsync(CancellationToken ct = default);
}

public interface IAuthRepository
{
    Task<Row?> FindCompanyAsync(string companyIdOrName, CancellationToken ct);
    Task<Row?> FindUserForLoginAsync(long companyId, string username, CancellationToken ct);
    Task<Row> LoginAsync(long companyId, string username, bool passwordOk, string? ip, string? device, string? userAgent, CancellationToken ct);
    Task<long> ValidateSessionAsync(long companyId, string token, CancellationToken ct);
    Task LogoutAsync(long companyId, string token, CancellationToken ct);
    Task<Row?> LoadUserAsync(long companyId, long userId, CancellationToken ct);
    Task<List<Row>> LoadStoresAsync(long companyId, long userId, bool allStores, CancellationToken ct);
    Task<List<string>> LoadPermissionsAsync(long companyId, long userId, CancellationToken ct);
    Task<List<string>> AllPermissionCodesAsync(CancellationToken ct);
    Task ChangePasswordAsync(long companyId, long userId, bool currentOk, string newHash, CancellationToken ct);
}

public interface IUserRepository
{
    Task<Page<Row>> ListAsync(long companyId, PageQuery q, string? status, CancellationToken ct);
    Task<Row?> GetAsync(long companyId, long id, CancellationToken ct);
    Task<long> InsertAsync(long companyId, string username, string hash, string fullName, string role, bool allStores, long? staffPartyId, CancellationToken ct);
    Task UpdateAsync(long companyId, long id, string fullName, string role, bool allStores, long? staffPartyId, CancellationToken ct);
    Task ReplaceStoresAsync(long companyId, long id, IReadOnlyCollection<long> storeIds, CancellationToken ct);
    Task ReplacePermissionsAsync(long companyId, long id, IReadOnlyCollection<string> codes, long grantedBy, CancellationToken ct);
    Task<List<Row>> PermissionCatalogAsync(CancellationToken ct);
    Task<List<Row>> RolesAsync(CancellationToken ct);
    Task AdminResetPasswordAsync(long companyId, long target, string tempHash, CancellationToken ct);
    Task SetStatusAsync(long companyId, long target, string status, CancellationToken ct);
    Task<List<Row>> LoginHistoryAsync(long companyId, long? userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<long> ActiveOwnerCountAsync(long companyId, CancellationToken ct);
}

public interface IStoreRepository
{
    Task<List<Row>> ListAsync(long companyId, bool includeInactive, CancellationToken ct);
    Task<Row?> GetAsync(long companyId, long id, CancellationToken ct);
    Task<long> InsertAsync(long companyId, string code, string name, long? cityId, string? address, string? phone, CancellationToken ct);
    Task<bool> UpdateAsync(long companyId, long id, string name, long? cityId, string? address, string? phone, bool isActive, long? rowVer, CancellationToken ct);
    Task<Row?> GetCompanyAsync(long companyId, CancellationToken ct);
    Task<bool> UpdateCompanyAsync(long companyId, CompanyUpdate u, CancellationToken ct);
    Task<List<Row>> DevicesAsync(long companyId, CancellationToken ct);
}

public sealed record CompanyUpdate(string Name, string? LegalName, string? Ntn, string? Strn, string? Timezone, string? RoundingMode,
                                   string? PriceBandBasis, string? InvoiceTerms, long? RowVer);

public interface IMasterRepository
{
    Task<List<Row>> ListAsync(string kind, string? search, bool includeInactive, CancellationToken ct);
    Task<Row?> GetAsync(string kind, long id, CancellationToken ct);
    Task<long> InsertAsync(string kind, long companyId, MasterInput m, CancellationToken ct);
    Task<bool> UpdateAsync(string kind, long id, MasterInput m, bool isActive, long? rowVer, CancellationToken ct);
    Task SoftDeleteAsync(string kind, long id, long userId, CancellationToken ct);
    Task<List<Row>> CategoriesAsync(string? search, long? departmentId, bool includeInactive, CancellationToken ct);
    Task<long> InsertCategoryAsync(long companyId, long departmentId, long? parentId, MasterInput m, CancellationToken ct);
    Task<bool> UpdateCategoryAsync(long id, long departmentId, long? parentId, MasterInput m, bool isActive, long? rowVer, CancellationToken ct);
    Task SoftDeleteCategoryAsync(long id, long userId, CancellationToken ct);
    Task<List<Row>> SizesAsync(long? sizeGroupId, CancellationToken ct);
    Task<long> InsertSizeAsync(long companyId, long sizeGroupId, string sizeCode, int sortOrder, CancellationToken ct);
    Task DeleteSizeAsync(long id, CancellationToken ct);
}

public sealed record MasterInput(string Name, string? NameUr, string? Code, int SortOrder);

public interface IPartyRepository
{
    Task<Page<Row>> ListAsync(PartyFilter f, PageQuery q, CancellationToken ct);
    Task<Row?> GetAsync(long id, CancellationToken ct);
    Task<long> InsertAsync(long companyId, PartyInput p, string code, CancellationToken ct);
    Task<bool> UpdateAsync(long id, PartyInput p, long? rowVer, CancellationToken ct);
    Task SoftDeleteAsync(long id, long userId, CancellationToken ct);
    Task<long> NextCodeNumberAsync(long companyId, long storeId, string series, CancellationToken ct);
    Task UpsertStaffAsync(long companyId, long partyId, StaffInput s, CancellationToken ct);
    Task<Row?> StaffAsync(long partyId, CancellationToken ct);
    Task MoveStaffAsync(long companyId, long partyId, long toStore, DateOnly effectiveOn, long userId, CancellationToken ct);
    Task<List<Row>> StaffHistoryAsync(long partyId, CancellationToken ct);
}

public sealed record PartyFilter(bool? Customer, bool? Vendor, bool? Staff, string? Status, bool OnlyWithBalance);

public sealed record PartyInput(string Name, string? NameUr, string? FatherName, string? Cnic, string? PhonePrimary, string? PhoneSecondary,
                                string? Email, string? Address, long? CityId, bool IsCustomer, bool IsVendor, bool IsStaff, long? CreditLimit,
                                string Status);

public sealed record StaffInput(string? Designation, bool IsManager, long BaseSalary, string PayFrequency, long CurrentStoreId, DateOnly? JoinedOn, DateOnly? LeftOn);
