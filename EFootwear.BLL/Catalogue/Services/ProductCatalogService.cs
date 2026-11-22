using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// The New Product screen (docx 4.1) and the product list's Delete (docx 4.2): create, change and delete a product with its sizes.
///
/// Rules: a product has at least one size; a barcode is unique in the company (blank = product code + S + size); prices are whole rupees; every
/// price that is set or changed writes a price event (Part 9.2); changing the company sale price of an existing size needs the extra
/// CHANGE_SELLING_PRICE right; a size or product that anything else still uses (stock, invoices, discount rules ...) cannot be deleted, it can be
/// switched to Inactive instead. After any change that touches prices or sizes the pricing engine is rebuilt in the same transaction, because the POS
/// reads promo.effective_prices.
/// </summary>
public sealed class ProductCatalogService(
    IProductRepository productRepository,
    IPriceHistoryRepository priceHistoryRepository,
    IBarcodeRepository barcodeRepository,
    IPricingEngineRepository pricingEngineRepository,
    IMasterListRepository masterListRepository,
    ICategoryRepository categoryRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductCatalogService> logger)
{
    private const string BarcodeChangeReason = PriceChangeReasonCodes.ProductEdit;

    public async Task<ProductDetailResponse> GetDetailAsync(Guid productId, CancellationToken cancellationToken) =>
        await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw ProductNotFound();

    public async Task<NextProductCodeResponse> GetNextProductCodeAsync(CancellationToken cancellationToken) =>
        new(await productRepository.PeekNextProductCodeAsync(cancellationToken));

    public async Task<PagedResult<PriceHistoryEntryResponse>> ListPriceHistoryAsync(Guid productId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        _ = await GetDetailAsync(productId, cancellationToken);
        return await priceHistoryRepository.ListByProductAsync(productId, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<ProductSavedResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var header = BuildHeader(request.Name, request.NameUrdu, request.DepartmentId, request.CategoryId, request.BrandId, request.SeasonId,
                                 request.GenderId, request.ProductTypeId, request.UnitId, request.LocationId, request.Weight, request.Description,
                                 request.TaxRate, request.TaxCode, request.Status);
        await EnsureReferencesAsync(header, existing: null, cancellationToken);
        var variants = NormalizeVariants(request.Variants, existingVariants: null);
        await EnsureVariantReferencesAsync(variants, existingVariants: null, cancellationToken);
        EnsureTypedBarcodesAreUnique(variants);

        var (productId, code) = await productRepository.InsertProductAsync(header, currentUser.UserId, cancellationToken);

        // The code exists now, so blank barcodes can be built. The checks run after the insert; an error rolls the whole request back.
        var writeData = variants.Select(variant => ToWriteData(variant, variant.TypedBarcode ?? VariantRules.BuildDefaultBarcode(code, variant.SizeCode), variant.Status)).ToList();
        EnsureBarcodesAreDistinct(writeData);
        await EnsureBarcodesAreFreeAsync(writeData.Select(data => data.Barcode).ToList(), [], cancellationToken);

        var priceEvents = new List<PriceEventWriteData>();
        foreach (var data in writeData)
        {
            var variantId = await productRepository.InsertVariantAsync(productId, data, currentUser.UserId, cancellationToken);
            priceEvents.Add(new PriceEventWriteData(variantId, Guid.Empty, PriceTypeCodes.Purchase, null, data.PurchasePrice, null, PriceChangeReasonCodes.InitialPrice));
            priceEvents.Add(new PriceEventWriteData(variantId, Guid.Empty, PriceTypeCodes.Sale, null, data.SalePrice, null, PriceChangeReasonCodes.InitialPrice));
        }
        await priceHistoryRepository.AddEventsAsync(priceEvents, currentUser.UserId, cancellationToken);
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Product {ProductId} ({ProductCode}) created in company {CompanyId} by user {UserId} with {VariantCount} sizes",
            productId, code, currentUser.CompanyId, currentUser.UserId, writeData.Count);
        return new ProductSavedResponse(productId, code);
    }

    public async Task UpdateAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var existing = await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw ProductNotFound();
        var header = BuildHeader(request.Name, request.NameUrdu, request.DepartmentId, request.CategoryId, request.BrandId, request.SeasonId,
                                 request.GenderId, request.ProductTypeId, request.UnitId, request.LocationId, request.Weight, request.Description,
                                 request.TaxRate, request.TaxCode, request.Status);
        await EnsureReferencesAsync(header, existing, cancellationToken);

        var snapshots = await productRepository.ListVariantsAsync(productId, cancellationToken);
        var variants = NormalizeVariants(request.Variants, snapshots);
        await EnsureVariantReferencesAsync(variants, snapshots, cancellationToken);
        EnsureTypedBarcodesAreUnique(variants);

        var writeData = variants.Select(variant =>
        {
            var snapshot = variant.VariantId is { } id ? snapshots.First(candidate => candidate.VariantId == id) : null;
            var barcode = variant.TypedBarcode ?? snapshot?.Barcode ?? VariantRules.BuildDefaultBarcode(existing.Code, variant.SizeCode);
            return ToWriteData(variant, barcode, variant.Status);
        }).ToList();
        EnsureBarcodesAreDistinct(writeData);
        await EnsureBarcodesAreFreeAsync(writeData.Select(data => data.Barcode).ToList(), snapshots.Select(snapshot => snapshot.VariantId).ToList(), cancellationToken);

        var salePriceChanges = writeData.Where(data => data.VariantId is not null
            && snapshots.First(snapshot => snapshot.VariantId == data.VariantId).SalePrice != data.SalePrice).ToList();
        if (salePriceChanges.Count > 0 && !currentUser.HasPermission(PermissionCodes.Products.NewProduct.ChangeSellingPrice))
            throw new DomainException(ErrorKind.Forbidden, "You are not allowed to change selling prices.", ErrorCodes.PermissionDenied);

        await productRepository.UpdateProductAsync(productId, request.RowVersion, header, cancellationToken);

        var keptVariantIds = writeData.Where(data => data.VariantId is not null).Select(data => data.VariantId!.Value).ToHashSet();
        var priceEvents = new List<PriceEventWriteData>();
        var barcodeHistory = new List<BarcodeHistoryWriteData>();
        var sizesOrPricesChanged = false;

        foreach (var data in writeData.Where(candidate => candidate.VariantId is not null))
        {
            var snapshot = snapshots.First(candidate => candidate.VariantId == data.VariantId);
            var structureChanged = snapshot.ColorId != data.ColorId || snapshot.SizeGroupId != data.SizeGroupId || snapshot.SizeCode != data.SizeCode
                                   || snapshot.Barcode != data.Barcode || snapshot.Status != data.Status;
            if (structureChanged)
            {
                await productRepository.UpdateVariantAsync(snapshot.VariantId, data, cancellationToken);
                sizesOrPricesChanged = true;
            }
            if (snapshot.Barcode != data.Barcode)
                barcodeHistory.Add(new BarcodeHistoryWriteData(snapshot.VariantId, snapshot.Barcode, data.Barcode, null, BarcodeChangeReason));

            if (snapshot.PurchasePrice != data.PurchasePrice || snapshot.SalePrice != data.SalePrice)
            {
                await productRepository.UpdateVariantPricesAsync(snapshot.VariantId, data.PurchasePrice, data.SalePrice, cancellationToken);
                sizesOrPricesChanged = true;
                if (snapshot.PurchasePrice != data.PurchasePrice)
                    priceEvents.Add(new PriceEventWriteData(snapshot.VariantId, Guid.Empty, PriceTypeCodes.Purchase, snapshot.PurchasePrice, data.PurchasePrice, null, PriceChangeReasonCodes.ProductEdit));
                if (snapshot.SalePrice != data.SalePrice)
                    priceEvents.Add(new PriceEventWriteData(snapshot.VariantId, Guid.Empty, PriceTypeCodes.Sale, snapshot.SalePrice, data.SalePrice, null, PriceChangeReasonCodes.ProductEdit));
            }
        }

        foreach (var removedSnapshot in snapshots.Where(snapshot => !keptVariantIds.Contains(snapshot.VariantId)))
        {
            await EnsureVariantIsFreeToDeleteAsync(existing.Code, removedSnapshot, cancellationToken);
            await productRepository.SoftDeleteVariantAsync(removedSnapshot.VariantId, currentUser.UserId, cancellationToken);
            sizesOrPricesChanged = true;
        }

        foreach (var data in writeData.Where(candidate => candidate.VariantId is null))
        {
            var variantId = await productRepository.InsertVariantAsync(productId, data, currentUser.UserId, cancellationToken);
            priceEvents.Add(new PriceEventWriteData(variantId, Guid.Empty, PriceTypeCodes.Purchase, null, data.PurchasePrice, null, PriceChangeReasonCodes.InitialPrice));
            priceEvents.Add(new PriceEventWriteData(variantId, Guid.Empty, PriceTypeCodes.Sale, null, data.SalePrice, null, PriceChangeReasonCodes.InitialPrice));
            sizesOrPricesChanged = true;
        }

        await priceHistoryRepository.AddEventsAsync(priceEvents, currentUser.UserId, cancellationToken);
        await barcodeRepository.AddHistoryAsync(barcodeHistory, currentUser.UserId, cancellationToken);
        if (sizesOrPricesChanged || existing.CategoryId != header.CategoryId)
            await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Product {ProductId} changed in company {CompanyId} by user {UserId}", productId, currentUser.CompanyId, currentUser.UserId);
    }

    public async Task DeleteAsync(Guid productId, CancellationToken cancellationToken)
    {
        var existing = await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw ProductNotFound();
        var snapshots = await productRepository.ListVariantsAsync(productId, cancellationToken);

        var productUsages = await rowUsageRepository.FindUsagesAsync(CatalogueOwnedTables.ProductsTable, productId, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"Product {existing.Code} \"{existing.Name}\"",
            productUsages.Where(usage => !CatalogueOwnedTables.OwnedByProduct.Contains(usage.TableName)).ToList());
        foreach (var snapshot in snapshots)
            await EnsureVariantIsFreeToDeleteAsync(existing.Code, snapshot, cancellationToken);

        foreach (var snapshot in snapshots)
            await productRepository.SoftDeleteVariantAsync(snapshot.VariantId, currentUser.UserId, cancellationToken);
        await productRepository.SoftDeleteProductAsync(productId, currentUser.UserId, cancellationToken);
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Product {ProductId} ({ProductCode}) deleted in company {CompanyId} by user {UserId}", productId, existing.Code, currentUser.CompanyId, currentUser.UserId);
    }

    // ---------- checks ----------

    private static ProductHeaderData BuildHeader(string? name, string? nameUrdu, Guid departmentId, Guid categoryId, Guid? brandId, Guid? seasonId,
                                                 Guid? genderId, Guid? productTypeId, Guid? unitId, Guid? locationId, decimal? weight,
                                                 string? description, decimal taxRate, string? taxCode, string? status)
    {
        var header = new ProductHeaderData(
            ProductRules.NormalizeName(name), ProductRules.NormalizeOptionalText(nameUrdu), departmentId, categoryId, brandId, seasonId, genderId,
            productTypeId, unitId, locationId, weight, ProductRules.NormalizeDescription(description), taxRate,
            ProductRules.NormalizeOptionalText(taxCode), string.IsNullOrWhiteSpace(status) ? ProductStatusCodes.Active : status.Trim().ToUpperInvariant());
        ProductRules.EnsureValidHeader(header.Name, header.NameUrdu, header.Description, header.Weight, header.TaxRate, header.TaxCode, header.Status);
        return header;
    }

    /// <summary>Checks that the chosen classification values exist and are active. A value the product already has is not re-checked: an old product can keep a value that was switched off later.</summary>
    private async Task EnsureReferencesAsync(ProductHeaderData header, ProductDetailResponse? existing, CancellationToken cancellationToken)
    {
        if (header.DepartmentId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a department.");
        if (header.CategoryId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a category.");

        if (existing is null || existing.DepartmentId != header.DepartmentId)
            await EnsureActiveItemAsync(MasterListDefinitions.Department, header.DepartmentId, cancellationToken);

        if (existing is null || existing.CategoryId != header.CategoryId || existing.DepartmentId != header.DepartmentId)
        {
            var category = await categoryRepository.FindAsync(header.CategoryId, cancellationToken)
                           ?? throw new DomainException(ErrorKind.Validation, "The category was not found.");
            if (!category.IsActive)
                throw new DomainException(ErrorKind.Validation, "Choose an active category.");
            if (category.DepartmentId != header.DepartmentId)
                throw new DomainException(ErrorKind.Validation, "The category does not belong to the chosen department.");
        }

        await EnsureOptionalActiveItemAsync(MasterListDefinitions.Brand, header.BrandId, existing?.BrandId, cancellationToken);
        await EnsureOptionalActiveItemAsync(MasterListDefinitions.Season, header.SeasonId, existing?.SeasonId, cancellationToken);
        await EnsureOptionalActiveItemAsync(MasterListDefinitions.Gender, header.GenderId, existing?.GenderId, cancellationToken);
        await EnsureOptionalActiveItemAsync(MasterListDefinitions.ProductType, header.ProductTypeId, existing?.ProductTypeId, cancellationToken);
        await EnsureOptionalActiveItemAsync(MasterListDefinitions.Unit, header.UnitId, existing?.UnitId, cancellationToken);
        await EnsureOptionalActiveItemAsync(MasterListDefinitions.Location, header.LocationId, existing?.LocationId, cancellationToken);
    }

    private async Task EnsureVariantReferencesAsync(IReadOnlyList<NormalizedVariantInput> variants, IReadOnlyList<ExistingVariantSnapshot>? existingVariants,
                                                    CancellationToken cancellationToken)
    {
        foreach (var variant in variants)
        {
            var snapshot = variant.VariantId is { } id ? existingVariants?.FirstOrDefault(candidate => candidate.VariantId == id) : null;
            await EnsureOptionalActiveItemAsync(MasterListDefinitions.Color, variant.ColorId, snapshot?.ColorId, cancellationToken);
            await EnsureOptionalActiveItemAsync(MasterListDefinitions.SizeGroup, variant.SizeGroupId, snapshot?.SizeGroupId, cancellationToken);
        }
    }

    private async Task EnsureOptionalActiveItemAsync(MasterListDefinition definition, Guid? itemId, Guid? currentItemId, CancellationToken cancellationToken)
    {
        if (itemId is null || itemId == currentItemId) return;
        await EnsureActiveItemAsync(definition, itemId.Value, cancellationToken);
    }

    private async Task EnsureActiveItemAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await masterListRepository.FindAsync(definition, itemId, cancellationToken);
        if (item is null || !item.IsActive)
            throw new DomainException(ErrorKind.Validation, $"Choose an active value for \"{definition.DisplayName}\".");
    }

    private static List<NormalizedVariantInput> NormalizeVariants(IReadOnlyList<ProductVariantInput>? inputs, IReadOnlyList<ExistingVariantSnapshot>? existingVariants)
    {
        if (inputs is null || inputs.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Add at least one size.");
        if (inputs.Count > VariantRules.MaximumVariantsPerProduct)
            throw new DomainException(ErrorKind.Validation, $"A product can have at most {VariantRules.MaximumVariantsPerProduct} sizes.");

        var normalized = new List<NormalizedVariantInput>();
        var seenVariantIds = new HashSet<Guid>();
        var seenSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            var sizeCode = VariantRules.NormalizeSizeCode(input.SizeCode);
            VariantRules.EnsureValidSizeCode(sizeCode);
            CataloguePriceRules.EnsureValidPrice(input.PurchasePrice, $"purchase price of size {sizeCode}");
            CataloguePriceRules.EnsureValidPrice(input.SalePrice, $"sale price of size {sizeCode}");

            string? typedBarcode = null;
            if (!string.IsNullOrWhiteSpace(input.Barcode))
            {
                typedBarcode = VariantRules.NormalizeBarcode(input.Barcode);
                VariantRules.EnsureValidBarcode(typedBarcode);
            }

            var status = string.IsNullOrWhiteSpace(input.Status) ? ProductStatusCodes.Active : input.Status.Trim().ToUpperInvariant();
            if (!ProductStatusCodes.IsKnown(status))
                throw new DomainException(ErrorKind.Validation, $"The status of size {sizeCode} must be Active or Inactive.");

            if (input.VariantId is { } variantId)
            {
                if (existingVariants is null || existingVariants.All(snapshot => snapshot.VariantId != variantId))
                    throw new DomainException(ErrorKind.Validation, $"Size {sizeCode} does not belong to this product.");
                if (!seenVariantIds.Add(variantId))
                    throw new DomainException(ErrorKind.Validation, $"Size {sizeCode} is listed twice.");
            }

            var sizeKey = $"{input.ColorId}|{sizeCode}";
            if (!seenSizes.Add(sizeKey))
                throw new DomainException(ErrorKind.Validation, $"Size {sizeCode} is listed twice for the same colour.");

            normalized.Add(new NormalizedVariantInput(input.VariantId, input.ColorId, input.SizeGroupId, sizeCode, typedBarcode,
                                                      input.PurchasePrice, input.SalePrice, input.RowVersion, status));
        }
        return normalized;
    }

    private static void EnsureTypedBarcodesAreUnique(IReadOnlyList<NormalizedVariantInput> variants)
    {
        var duplicate = variants.Where(variant => variant.TypedBarcode is not null).GroupBy(variant => variant.TypedBarcode!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new DomainException(ErrorKind.Validation, $"The barcode {duplicate.Key} is used for more than one size.");
    }

    private static void EnsureBarcodesAreDistinct(IReadOnlyList<ProductVariantWriteData> data)
    {
        var duplicate = data.GroupBy(item => item.Barcode, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new DomainException(ErrorKind.Validation,
                $"The barcode {duplicate.Key} would be used for more than one size. Type a barcode for each size, for example when a size exists in several colours.");
    }

    private async Task EnsureBarcodesAreFreeAsync(IReadOnlyCollection<string> barcodes, IReadOnlyCollection<Guid> exceptVariantIds, CancellationToken cancellationToken)
    {
        var inUse = await productRepository.FindBarcodesInUseAsync(barcodes, exceptVariantIds, cancellationToken);
        if (inUse.Count > 0)
            throw new DomainException(ErrorKind.Conflict, $"The barcode {string.Join(", ", inUse)} is already used by another product.", ErrorCodes.DatabaseUniqueViolation);
    }

    private async Task EnsureVariantIsFreeToDeleteAsync(string productCode, ExistingVariantSnapshot snapshot, CancellationToken cancellationToken)
    {
        var usages = await rowUsageRepository.FindUsagesAsync(CatalogueOwnedTables.VariantsTable, snapshot.VariantId, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"Size {snapshot.SizeCode} of product {productCode} (set it to Inactive instead)",
            usages.Where(usage => !CatalogueOwnedTables.OwnedByVariant.Contains(usage.TableName)).ToList());
    }

    private static ProductVariantWriteData ToWriteData(NormalizedVariantInput variant, string barcode, string status) =>
        new(variant.VariantId, variant.ColorId, variant.SizeGroupId, variant.SizeCode, barcode, status, variant.PurchasePrice, variant.SalePrice, variant.RowVersion);

    private static DomainException ProductNotFound() => new(ErrorKind.NotFound, "The product was not found.");

    private sealed record NormalizedVariantInput(Guid? VariantId, Guid? ColorId, Guid? SizeGroupId, string SizeCode, string? TypedBarcode,
                                                 long PurchasePrice, long SalePrice, long? RowVersion, string Status);
}
