using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// Change Product Attributes (docx 4.6): every product that has the old value in a classification field gets the new value in one step. The new
/// value must exist and be active. Changing the category also moves the product to that category's department. The update is naturally repeatable:
/// a second run finds no product with the old value.
/// </summary>
public sealed class ProductAttributeService(
    IProductAttributeRepository productAttributeRepository,
    IMasterListRepository masterListRepository,
    ICategoryRepository categoryRepository,
    IPricingEngineRepository pricingEngineRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductAttributeService> logger)
{
    public async Task<PagedResult<ProductAttributeRowResponse>> ListProductsAsync(string? fieldType, Guid oldValueId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureFieldType(fieldType);
        return await productAttributeRepository.ListProductsWithValueAsync(fieldType!, oldValueId, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<AttributeChangeAppliedResponse> ApplyAsync(ChangeProductAttributeRequest request, CancellationToken cancellationToken)
    {
        EnsureFieldType(request.FieldType);
        if (request.OldValueId == Guid.Empty || request.NewValueId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose the old and the new value.");
        if (request.OldValueId == request.NewValueId)
            throw new DomainException(ErrorKind.Validation, "The new value is the same as the old value.");
        await EnsureNewValueIsActiveAsync(request.FieldType, request.NewValueId, cancellationToken);

        var (changedCount, skippedProducts) = await productAttributeRepository.ApplyAsync(request.FieldType, request.OldValueId, request.NewValueId, cancellationToken);
        // A category change can move products in or out of category-wide discount rules.
        if (changedCount > 0 && request.FieldType == ProductAttributeFieldCodes.Category)
            await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Attribute {FieldType} changed on {ChangedCount} products in company {CompanyId} by user {UserId}",
            request.FieldType, changedCount, currentUser.CompanyId, currentUser.UserId);
        return new AttributeChangeAppliedResponse(changedCount, skippedProducts);
    }

    private static void EnsureFieldType(string? fieldType)
    {
        if (!ProductAttributeFieldCodes.IsKnown(fieldType))
            throw new DomainException(ErrorKind.Validation, "Choose which field to change.");
    }

    private async Task EnsureNewValueIsActiveAsync(string fieldType, Guid newValueId, CancellationToken cancellationToken)
    {
        if (fieldType == ProductAttributeFieldCodes.Category)
        {
            var category = await categoryRepository.FindAsync(newValueId, cancellationToken);
            if (category is null || !category.IsActive)
                throw new DomainException(ErrorKind.Validation, "Choose an active category as the new value.");
            return;
        }

        var definition = fieldType switch
        {
            ProductAttributeFieldCodes.Unit => MasterListDefinitions.Unit,
            ProductAttributeFieldCodes.Brand => MasterListDefinitions.Brand,
            ProductAttributeFieldCodes.Gender => MasterListDefinitions.Gender,
            ProductAttributeFieldCodes.Season => MasterListDefinitions.Season,
            ProductAttributeFieldCodes.Color => MasterListDefinitions.Color,
            ProductAttributeFieldCodes.Location => MasterListDefinitions.Location,
            ProductAttributeFieldCodes.ProductType => MasterListDefinitions.ProductType,
            _ => throw new DomainException(ErrorKind.Validation, "Choose which field to change."),
        };
        var item = await masterListRepository.FindAsync(definition, newValueId, cancellationToken);
        if (item is null || !item.IsActive)
            throw new DomainException(ErrorKind.Validation, $"Choose an active value for \"{definition.DisplayName}\" as the new value.");
    }
}
