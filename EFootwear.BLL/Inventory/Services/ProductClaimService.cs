using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>
/// Product Claim (docx 4.16) and Claim Approval (docx 4.17). A claim is a request; stock moves only when it is approved (by the baseline function, which also
/// checks the claim approval permission). Approving several claims is all or nothing. Not decided and not enforced: the effect of a claim on a vendor account
/// or a credit note, a claim value limit above which a second approver is needed.
/// </summary>
public sealed class ProductClaimService(
    IInventoryPostingRepository postingRepository,
    IInventoryReadRepository readRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductClaimService> logger)
{
    public const int MaximumClaimsPerApproval = 200;

    public async Task<ProductClaimResponse> CreateAsync(CreateProductClaimRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.StoreId);
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        InventoryRequestValidator.EnsureDateIsGiven(request.ClaimDate, "the claim date");
        InventoryRequestValidator.EnsureVariantIsGiven(request.VariantId, 1);
        InventoryRules.EnsureQuantityIsValid(request.Quantity, "The quantity");
        if (!ProductClaimReasonCodes.IsValid(request.ReasonCode))
            throw new DomainException(ErrorKind.Validation, "Choose the reason of the claim.");
        var note = InventoryRules.CleanOptionalText(request.Note, InventoryRules.DescriptionMaximumLength, "The description");

        var claimId = await postingRepository.RequestClaimAsync(request.StoreId, request.ClaimDate, request.ClientTransactionId, request.VariantId, request.Quantity,
            request.ReasonCode, note, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var claim = await GetAsync(claimId, cancellationToken);
        logger.LogInformation("Product claim {ClaimNumber} ({ClaimId}) of {Quantity} pieces requested in store {StoreId} of company {CompanyId} by user {UserId}",
            claim.ClaimNumber, claim.Id, claim.Quantity, claim.StoreId, currentUser.CompanyId, currentUser.UserId);
        return claim;
    }

    public async Task<ApproveProductClaimsResponse> ApproveAsync(ApproveProductClaimsRequest request, CancellationToken cancellationToken)
    {
        var claimIds = (request.ClaimIds ?? []).Distinct().ToList();
        if (claimIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Select at least one claim to approve.");
        if (claimIds.Count > MaximumClaimsPerApproval)
            throw new DomainException(ErrorKind.Validation, $"Approve at most {MaximumClaimsPerApproval} claims at a time.");

        // Look at every claim first so one answer names the claim that cannot be approved, before anything is changed.
        var claims = new List<ProductClaimResponse>();
        foreach (var claimId in claimIds)
        {
            var claim = await GetAsync(claimId, cancellationToken);
            if (claim.StatusCode != ProductClaimStatusCodes.Requested)
                throw new DomainException(ErrorKind.Conflict, $"Claim {claim.ClaimNumber} is already {claim.StatusCode.ToLowerInvariant()}.");
            claims.Add(claim);
        }
        foreach (var claim in claims.OrderBy(claim => claim.VariantId).ThenBy(claim => claim.StoreId))
            await postingRepository.ApproveClaimAsync(claim.Id, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{ClaimCount} product claims approved in company {CompanyId} by user {UserId}", claims.Count, currentUser.CompanyId, currentUser.UserId);
        return new ApproveProductClaimsResponse(claims.Count);
    }

    public async Task<ProductClaimResponse> RejectAsync(Guid claimId, RejectProductClaimRequest request, CancellationToken cancellationToken)
    {
        var reason = InventoryRules.CleanRequiredReason(request.Reason, "The reason for rejecting the claim");
        var claim = await GetAsync(claimId, cancellationToken);
        if (claim.StatusCode != ProductClaimStatusCodes.Requested)
            throw new DomainException(ErrorKind.Conflict, $"Claim {claim.ClaimNumber} is already {claim.StatusCode.ToLowerInvariant()}.");

        await postingRepository.RejectClaimAsync(claimId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Product claim {ClaimNumber} ({ClaimId}) rejected in store {StoreId} of company {CompanyId} by user {UserId}: {Reason}",
            claim.ClaimNumber, claimId, claim.StoreId, currentUser.CompanyId, currentUser.UserId, reason);
        return await GetAsync(claimId, cancellationToken);
    }

    public async Task<ProductClaimResponse> GetAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await readRepository.FindClaimAsync(claimId, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The product claim was not found.");
        InventoryRequestValidator.EnsureStoreAccess(currentUser, claim.StoreId);
        return claim;
    }

    public async Task<PagedResult<ProductClaimResponse>> SearchAsync(ProductClaimSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.SearchClaimsAsync(filter, pageRequest.Normalized(), cancellationToken);
    }
}
