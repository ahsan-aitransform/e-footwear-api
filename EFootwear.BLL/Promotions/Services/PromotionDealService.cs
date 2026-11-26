using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.Promotions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Promotions.Services;

/// <summary>
/// Deals (docx 2.12, Part 18): one barcode that sells several products, each at its net price; the price of the whole deal is the sum of quantity times net price. The deal gets its
/// own barcode when none is typed, and that barcode can never belong to a size or to another deal. A deal belongs to the whole company (the tables hold no store list), so only
/// people who may work in every store change one. A deleted deal stays in the database. [BUSINESS DECISION REQUIRED] How a deal is sold at the till (scanning its barcode, and which
/// stock it takes) is not decided; nothing in the sales module reads deals yet.
/// </summary>
public sealed class PromotionDealService(
    IPromotionDealRepository promotionDealRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PromotionDealService> logger)
{
    public Task<IReadOnlyList<DealResponse>> ListAsync(CancellationToken cancellationToken) => promotionDealRepository.ListAsync(cancellationToken);

    public async Task<DealResponse> GetAsync(Guid dealId, CancellationToken cancellationToken) =>
        await promotionDealRepository.FindAsync(dealId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The deal was not found.");

    public async Task<DealVariantLookupResponse> LookupVariantAsync(string? barcode, CancellationToken cancellationToken)
    {
        var normalized = VariantRules.NormalizeBarcode(barcode);
        VariantRules.EnsureValidBarcode(normalized);
        return await promotionDealRepository.FindVariantByBarcodeAsync(normalized, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, $"No product has the barcode {normalized}.");
    }

    public async Task<DealResponse> CreateAsync(SaveDealRequest request, CancellationToken cancellationToken)
    {
        EnsureMayChange();
        var (name, barcode, bundlePrice) = await CleanAndCheckAsync(request, cancellationToken);
        if (barcode.Length == 0) barcode = await promotionDealRepository.BuildFreeBarcodeAsync(cancellationToken);

        var dealId = await promotionDealRepository.CreateAsync(request with { Name = name }, barcode, bundlePrice, request.IsActive, currentUser.UserId, cancellationToken)
                     ?? throw new DomainException(ErrorKind.Conflict, $"The barcode {barcode} is already used by a product or another deal.");
        var deal = await GetAsync(dealId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Deal {DealId} created by user {UserId}", dealId, currentUser.UserId);
        return deal;
    }

    public async Task<DealResponse> UpdateAsync(Guid dealId, SaveDealRequest request, CancellationToken cancellationToken)
    {
        EnsureMayChange();
        if (request.RowVersion is not { } expectedRowVersion)
            throw new DomainException(ErrorKind.Validation, "Open the deal again before saving it: its version is missing.");
        var existing = await GetAsync(dealId, cancellationToken);

        var (name, barcode, bundlePrice) = await CleanAndCheckAsync(request, cancellationToken);
        if (barcode.Length == 0) barcode = existing.Barcode;

        if (!await promotionDealRepository.UpdateAsync(dealId, request with { Name = name }, barcode, bundlePrice, expectedRowVersion, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, "Somebody else changed this deal. Open it again and repeat your change.");
        var deal = await GetAsync(dealId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Deal {DealId} changed by user {UserId}", dealId, currentUser.UserId);
        return deal;
    }

    public async Task DeleteAsync(Guid dealId, CancellationToken cancellationToken)
    {
        EnsureMayChange();
        if (!await promotionDealRepository.DeleteAsync(dealId, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "The deal was not found.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Deal {DealId} deleted by user {UserId}", dealId, currentUser.UserId);
    }

    private async Task<(string Name, string Barcode, long BundlePrice)> CleanAndCheckAsync(SaveDealRequest request, CancellationToken cancellationToken)
    {
        var name = PromotionRules.NormalizeName(request.Name);
        var barcode = VariantRules.NormalizeBarcode(request.Barcode);
        PromotionRules.EnsureWindowIsValid(request.StartsAt, request.EndsAt);
        PromotionRules.EnsureDealIsValid(name, barcode, request.Lines.Select(line => (line.VariantId, line.Quantity, line.NetUnitPrice)).ToList());

        var variantIds = request.Lines.Select(line => line.VariantId).ToList();
        if (await promotionDealRepository.CountExistingVariantsAsync(variantIds, cancellationToken) != variantIds.Count)
            throw new DomainException(ErrorKind.Validation, "One of the products of the deal does not exist.");

        long bundlePrice;
        try
        {
            bundlePrice = request.Lines.Sum(line => checked((long)line.Quantity * line.NetUnitPrice));
        }
        catch (OverflowException)
        {
            throw new DomainException(ErrorKind.Validation, "The total of the deal is too large.");
        }
        return (name, barcode, bundlePrice);
    }

    private void EnsureMayChange()
    {
        if (!currentUser.HasAccessToAllStores)
            throw new DomainException(ErrorKind.Forbidden, "A deal belongs to every store, so only somebody who may work in every store can change it.");
    }
}
