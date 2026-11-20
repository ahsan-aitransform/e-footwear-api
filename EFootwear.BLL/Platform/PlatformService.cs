using System.Text.RegularExpressions;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Stores;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform;

public sealed record CompanySummary(Guid Id, string Name, string Status, string Timezone, string CurrencyCode, DateTimeOffset CreatedAt);

public sealed record CreateCompanyRequest(string Name, string? Timezone, string OwnerEmail, string OwnerFullName,
                                          string OwnerPassword, string FirstStoreCode, string FirstStoreName);

public sealed record CreatedCompany(Guid CompanyId, Guid FirstStoreId, Guid OwnerUserId);

public sealed record CreateStoreRequest(string Code, string Name);

/// <summary>Platform-level data access: companies and the administrator audit trail.</summary>
public interface IPlatformRepository
{
    Task<IReadOnlyList<CompanySummary>> ListCompaniesAsync(CancellationToken ct);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct);

    /// <summary>Creates the company, its first store and its owner in one database function.</summary>
    Task<CreatedCompany> CreateCompanyAsync(Guid adminId, CreateCompanyRequest request, string ownerPasswordHash, CancellationToken ct);

    Task LogAdminActionAsync(Guid adminId, string action, Guid? companyId, object detail, CancellationToken ct);
}

/// <summary>
/// Platform administrator use cases: register companies and their first store and owner, add stores.
/// Only a platform administrator may call these. Company users never reach this code.
/// </summary>
public sealed partial class PlatformService(IPlatformRepository platform, IStoreRepository stores, EFootwear.BLL.Users.IUserRepository users,
                                            IPasswordHasher hasher, RequestContext context, IUnitOfWork uow, ILogger<PlatformService> log)
{
    [GeneratedRegex("^[A-Za-z0-9-]{1,20}$")]
    private static partial Regex StoreCodePattern();

    public async Task<IReadOnlyList<CompanySummary>> ListCompaniesAsync(CancellationToken ct)
    {
        RequirePlatformAdmin();
        return await platform.ListCompaniesAsync(ct);
    }

    public async Task<CreatedCompany> CreateCompanyAsync(CreateCompanyRequest req, CancellationToken ct)
    {
        RequirePlatformAdmin();

        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 120)
            throw new DomainException(ErrorKind.Validation, "Company name is required (up to 120 characters).");
        UserCreationRules.EnsureValidAccount(req.OwnerEmail, req.OwnerFullName, req.OwnerPassword);
        ValidateStore(req.FirstStoreCode, req.FirstStoreName);

        if (await users.EmailExistsAsync(req.OwnerEmail.Trim().ToLowerInvariant(), ct))
            throw new DomainException(ErrorKind.Conflict, $"The email {req.OwnerEmail.Trim()} is already in use.");

        var hash = hasher.Hash(req.OwnerPassword);
        var created = await platform.CreateCompanyAsync(context.PlatformAdminId!.Value, req with
        {
            Name = req.Name.Trim(),
            OwnerEmail = req.OwnerEmail.Trim().ToLowerInvariant(),
            OwnerFullName = req.OwnerFullName.Trim(),
            FirstStoreCode = req.FirstStoreCode.Trim(),
            FirstStoreName = req.FirstStoreName.Trim(),
        }, hash, ct);
        await uow.CommitAsync(ct);
        log.LogInformation("Company {CompanyId} created with first store {StoreId} and owner {OwnerUserId} by platform admin {AdminId}",
            created.CompanyId, created.FirstStoreId, created.OwnerUserId, context.PlatformAdminId);
        return created;
    }

    public async Task<IReadOnlyList<StoreDto>> ListStoresAsync(Guid companyId, CancellationToken ct)
    {
        RequirePlatformAdmin();
        await EnsureCompanyExistsAsync(companyId, ct);
        return await stores.ListForCompanyAsync(companyId, ct);
    }

    public async Task<Guid> CreateStoreAsync(Guid companyId, CreateStoreRequest req, CancellationToken ct)
    {
        RequirePlatformAdmin();
        await EnsureCompanyExistsAsync(companyId, ct);
        ValidateStore(req.Code, req.Name);

        var code = req.Code.Trim();
        if (await stores.CodeExistsAsync(companyId, code, ct))
            throw new DomainException(ErrorKind.Conflict, $"Store code {code} already exists in this company.");

        var id = await stores.CreateAsync(companyId, code, req.Name.Trim(), ct);
        await platform.LogAdminActionAsync(context.PlatformAdminId!.Value, "STORE_CREATED", companyId,
                                           new { storeId = id, code }, ct);
        await uow.CommitAsync(ct);
        log.LogInformation("Store {StoreId} ({Code}) created in company {CompanyId} by platform admin {AdminId}",
            id, code, companyId, context.PlatformAdminId);
        return id;
    }

    private void RequirePlatformAdmin()
    {
        if (!context.IsPlatformAdmin)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
    }

    private async Task EnsureCompanyExistsAsync(Guid companyId, CancellationToken ct)
    {
        if (companyId == Guid.Empty || !await platform.CompanyExistsAsync(companyId, ct))
            throw new DomainException(ErrorKind.NotFound, "Company not found.");
    }

    private static void ValidateStore(string code, string name)
    {
        if (!StoreCodePattern().IsMatch((code ?? "").Trim()))
            throw new DomainException(ErrorKind.Validation, "Store code: 1 to 20 letters, digits or dashes.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new DomainException(ErrorKind.Validation, "Store name is required (up to 120 characters).");
    }
}
