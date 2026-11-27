using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.Promotions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Promotions.Services;

/// <summary>
/// The products of a special offer (docx 2.14 Special Offer Details): scan a barcode, or add every size of a category or a company (brand) at one percentage, change the percentage
/// of a line, remove a line. A size is in an offer once. Adding a whole category or brand takes the sizes that exist now; products made later are not added. Every change runs
/// the pricing engine, so a running offer shows its new prices at once.
/// </summary>
public sealed class SpecialOfferLineService(
    IPromotionRuleRepository promotionRuleRepository,
    IPricingEngineRepository pricingEngineRepository,
    StoreAccessGuard storeAccessGuard,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<SpecialOfferLineService> logger)
{
    public async Task<IReadOnlyList<SpecialOfferLineResponse>> ListAsync(Guid offerId, CancellationToken cancellationToken)
    {
        await FindOfferAsync(offerId, cancellationToken);
        return await promotionRuleRepository.ListLinesAsync(offerId, cancellationToken);
    }

    public async Task<AddSpecialOfferLinesResponse> AddAsync(Guid offerId, AddSpecialOfferLinesRequest request, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(offerId, cancellationToken);
        await EnsureMayChangeAsync(offer, cancellationToken);
        PromotionRules.EnsurePercentageIsValid(request.DiscountPercent);

        var barcode = VariantRules.NormalizeBarcode(request.Barcode);
        var sources = (barcode.Length > 0 ? 1 : 0) + (request.CategoryId is not null ? 1 : 0) + (request.BrandId is not null ? 1 : 0);
        if (sources != 1) throw new DomainException(ErrorKind.Validation, "Scan one barcode or product code, or choose one category, or choose one company.");
        if (barcode.Length > 0) VariantRules.EnsureValidBarcode(barcode);

        var (matchedCount, addedCount) = await promotionRuleRepository.AddLinesAsync(
            offerId, barcode.Length > 0 ? barcode : null, request.CategoryId, request.BrandId, request.DiscountPercent!.Value, cancellationToken);
        if (matchedCount == 0)
            throw new DomainException(barcode.Length > 0 ? ErrorKind.NotFound : ErrorKind.Validation,
                                      barcode.Length > 0 ? $"No product has the barcode or product code {barcode}." : "That category or company has no products yet.");

        if (addedCount > 0) await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{AddedCount} sizes added to special offer {OfferId} by user {UserId}", addedCount, offerId, currentUser.UserId);
        return new AddSpecialOfferLinesResponse(addedCount, matchedCount - addedCount);
    }

    public async Task UpdateAsync(Guid offerId, Guid lineId, UpdateSpecialOfferLineRequest request, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(offerId, cancellationToken);
        await EnsureMayChangeAsync(offer, cancellationToken);
        PromotionRules.EnsurePercentageIsValid(request.DiscountPercent);
        if (!await promotionRuleRepository.UpdateLineAsync(offerId, lineId, request.DiscountPercent!.Value, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "That product is not in this offer.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Line {LineId} of special offer {OfferId} changed by user {UserId}", lineId, offerId, currentUser.UserId);
    }

    public async Task DeleteAsync(Guid offerId, Guid lineId, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(offerId, cancellationToken);
        await EnsureMayChangeAsync(offer, cancellationToken);
        if (!await promotionRuleRepository.DeleteLineAsync(offerId, lineId, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "That product is not in this offer.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Line {LineId} removed from special offer {OfferId} by user {UserId}", lineId, offerId, currentUser.UserId);
    }

    private async Task<PromotionRuleResponse> FindOfferAsync(Guid offerId, CancellationToken cancellationToken) =>
        await promotionRuleRepository.FindAsync(PromotionKindCodes.SpecialOffer, offerId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The special offer was not found.");

    private async Task EnsureMayChangeAsync(PromotionRuleResponse offer, CancellationToken cancellationToken)
    {
        if (offer.Stores.Count == 0)
        {
            if (!currentUser.HasAccessToAllStores)
                throw new DomainException(ErrorKind.Forbidden, "An offer for every store can only be changed by somebody who may work in every store.");
            return;
        }
        foreach (var store in offer.Stores) await storeAccessGuard.EnsureAccessibleAsync(store.StoreId, cancellationToken);
    }
}
