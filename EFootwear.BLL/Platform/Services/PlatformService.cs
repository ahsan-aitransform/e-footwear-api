using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using EFootwear.Domain.Platform;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform.Services;

/// <summary>
/// Platform administrator use cases: register companies and their first store and owner, add stores.
/// Only a platform administrator may call these. Company users never reach this code.
/// </summary>
public sealed class PlatformService(
    IPlatformRepository platformRepository,
    IStoreRepository storeRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<PlatformService> logger)
{
    public const string StoreCreatedActionCode = "STORE_CREATED";

    public async Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        return await platformRepository.ListCompaniesAsync(cancellationToken);
    }

    public async Task<CreateCompanyResponse> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();

        CompanyRules.EnsureValidName(request.Name);
        UserCreationRules.EnsureValidAccount(request.OwnerEmail, request.OwnerFullName, request.OwnerPassword);
        StoreRules.EnsureValid(request.FirstStoreCode, request.FirstStoreName);

        if (await userRepository.EmailExistsAsync(request.OwnerEmail.Trim().ToLowerInvariant(), cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"The email {request.OwnerEmail.Trim()} is already in use.");

        var ownerPasswordHash = passwordHasher.Hash(request.OwnerPassword);
        var created = await platformRepository.CreateCompanyAsync(requestContext.PlatformAdministratorId!.Value, request with
        {
            Name = request.Name.Trim(),
            OwnerEmail = request.OwnerEmail.Trim().ToLowerInvariant(),
            OwnerFullName = request.OwnerFullName.Trim(),
            FirstStoreCode = request.FirstStoreCode.Trim(),
            FirstStoreName = request.FirstStoreName.Trim(),
        }, ownerPasswordHash, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Company {CompanyId} created with first store {StoreId} and owner {OwnerUserId} by platform admin {AdministratorId}",
            created.CompanyId, created.FirstStoreId, created.OwnerUserId, requestContext.PlatformAdministratorId);
        return created;
    }

    public async Task<IReadOnlyList<StoreResponse>> ListStoresAsync(Guid companyId, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        await EnsureCompanyExistsAsync(companyId, cancellationToken);
        return await storeRepository.ListForCompanyAsync(companyId, cancellationToken);
    }

    public async Task<Guid> CreateStoreAsync(Guid companyId, CreateStoreRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        await EnsureCompanyExistsAsync(companyId, cancellationToken);
        StoreRules.EnsureValid(request.Code, request.Name);

        var code = request.Code.Trim();
        if (await storeRepository.CodeExistsAsync(companyId, code, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Store code {code} already exists in this company.");

        var storeId = await storeRepository.CreateAsync(companyId, code, request.Name.Trim(), cancellationToken);
        await platformRepository.LogAdministratorActionAsync(requestContext.PlatformAdministratorId!.Value,
            StoreCreatedActionCode, companyId, new { storeId, code }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Store {StoreId} ({Code}) created in company {CompanyId} by platform admin {AdministratorId}",
            storeId, code, companyId, requestContext.PlatformAdministratorId);
        return storeId;
    }

    private void RequirePlatformAdministrator()
    {
        if (!requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
    }

    private async Task EnsureCompanyExistsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || !await platformRepository.CompanyExistsAsync(companyId, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "Company not found.");
    }
}
