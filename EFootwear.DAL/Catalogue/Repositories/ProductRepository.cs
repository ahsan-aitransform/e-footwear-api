using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Products, sizes and their company list prices with EF Core. The code is allocated by catalog.allocate_next_product_code() (company-wide,
/// works for store-restricted users). Ids are created here as time-ordered version 7 uuids, like the database default, so a product and its sizes
/// can be inserted without reading ids back. Changes are saved when the use case commits, in one transaction.
/// </summary>
public sealed class ProductRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductRepository
{
    public async Task<ProductDetailResponse?> FindDetailAsync(Guid productId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var product = await databaseContext.Products.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == productId && !candidate.IsDeleted, cancellationToken);
        if (product is null) return null;

        var variants = (await ListVariantsAsync(productId, cancellationToken))
            .Select(variant => new ProductVariantDetailResponse(variant.VariantId, variant.ColorId, variant.SizeGroupId, variant.SizeCode, variant.Barcode,
                                                                variant.Status, variant.PurchasePrice, variant.SalePrice, variant.RowVersion))
            .ToList();
        var slots = await databaseContext.ProductImages.AsNoTracking()
            .Where(image => image.CompanyId == companyId && image.ProductId == productId).OrderBy(image => image.Slot)
            .Select(image => new ProductImageResponse(image.Slot)).ToListAsync(cancellationToken);

        return new ProductDetailResponse(product.Id, product.Code, product.Name, product.NameUrdu, product.DepartmentId, product.CategoryId, product.BrandId,
            product.SeasonId, product.GenderId, product.ProductTypeId, product.UnitId, product.LocationId, product.Weight, product.Description, product.TaxRate,
            product.TaxCode, product.Status, product.CreatedAt, product.RowVersion, variants, slots);
    }

    public async Task<string> PeekNextProductCodeAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Database.SqlQuery<string>($"select catalog.peek_next_product_code() as \"Value\"").SingleAsync(cancellationToken);
    }

    public async Task<(Guid ProductId, string Code)> InsertProductAsync(ProductHeaderData header, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var code = await databaseContext.Database.SqlQuery<string>($"select catalog.allocate_next_product_code() as \"Value\"").SingleAsync(cancellationToken);
        var product = new Product { CompanyId = currentUser.CompanyId, Id = Guid.CreateVersion7(), Code = code, CreatedBy = createdBy };
        ApplyHeader(product, header);
        databaseContext.Products.Add(product);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return (product.Id, code);
    }

    public async Task UpdateProductAsync(Guid productId, long rowVersion, ProductHeaderData header, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var product = await databaseContext.Products.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == productId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");
        ApplyHeader(product, header);
        var entry = databaseContext.Entry(product);
        entry.Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        // An unchanged header still checks the version and bumps it, so two people saving the same product cannot both win.
        entry.Property(candidate => candidate.Name).IsModified = true;
    }

    public async Task<IReadOnlyList<ExistingVariantSnapshot>> ListVariantsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await (from variant in databaseContext.ProductVariants.AsNoTracking()
                      join price in databaseContext.VariantPrices.AsNoTracking()
                          on new { variant.CompanyId, VariantId = variant.Id } equals new { price.CompanyId, price.VariantId }
                      where variant.CompanyId == companyId && variant.ProductId == productId && !variant.IsDeleted
                      orderby variant.CreatedAt, variant.Id
                      select new ExistingVariantSnapshot(variant.Id, variant.ColorId, variant.SizeGroupId, variant.SizeCode, variant.Barcode, variant.Status,
                                                         price.PurchasePrice, price.SalePrice, variant.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> InsertVariantAsync(Guid productId, ProductVariantWriteData data, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variant = new ProductVariant
        {
            CompanyId = companyId, Id = Guid.CreateVersion7(), ProductId = productId, ColorId = data.ColorId, SizeGroupId = data.SizeGroupId,
            SizeCode = data.SizeCode, Barcode = data.Barcode, Status = data.Status, CreatedBy = createdBy,
        };
        databaseContext.ProductVariants.Add(variant);
        // EF Core is not told about the foreign keys between sizes, prices and events, so it would insert them in any order. The size is saved first;
        // the price row and the history rows that follow refer to it.
        await databaseContext.SaveChangesAsync(cancellationToken);
        databaseContext.VariantPrices.Add(new VariantPrice
        {
            CompanyId = companyId, VariantId = variant.Id, PurchasePrice = data.PurchasePrice, SalePrice = data.SalePrice,
            UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = createdBy,
        });
        return variant.Id;
    }

    public async Task UpdateVariantAsync(Guid variantId, ProductVariantWriteData data, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variant = await databaseContext.ProductVariants.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == variantId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The size was not found.");
        variant.ColorId = data.ColorId;
        variant.SizeGroupId = data.SizeGroupId;
        variant.SizeCode = data.SizeCode;
        variant.Barcode = data.Barcode;
        variant.Status = data.Status;
        if (data.RowVersion is { } rowVersion)
            databaseContext.Entry(variant).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
    }

    public async Task UpdateVariantPricesAsync(Guid variantId, long purchasePrice, long salePrice, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var price = await databaseContext.VariantPrices.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.VariantId == variantId, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The size has no price row.");
        price.PurchasePrice = purchasePrice;
        price.SalePrice = salePrice;
        price.UpdatedAt = DateTimeOffset.UtcNow;
        price.UpdatedBy = currentUser.UserId;
    }

    public async Task SoftDeleteVariantAsync(Guid variantId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variant = await databaseContext.ProductVariants.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == variantId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The size was not found.");
        variant.IsDeleted = true;
        variant.DeletedAt = DateTimeOffset.UtcNow;
        variant.DeletedBy = deletedBy;
    }

    public async Task SoftDeleteProductAsync(Guid productId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var product = await databaseContext.Products.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == productId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");
        product.IsDeleted = true;
        product.DeletedAt = DateTimeOffset.UtcNow;
        product.DeletedBy = deletedBy;
    }

    public async Task<IReadOnlyList<string>> FindBarcodesInUseAsync(IReadOnlyCollection<string> barcodes, IReadOnlyCollection<Guid> exceptVariantIds,
                                                                    CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0) return [];
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var barcodeArray = barcodes.ToArray();
        var exceptArray = exceptVariantIds.ToArray();

        var usedByVariants = await databaseContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.CompanyId == companyId && !variant.IsDeleted && barcodeArray.Contains(variant.Barcode) && !exceptArray.Contains(variant.Id))
            .Select(variant => variant.Barcode).ToListAsync(cancellationToken);
        var usedByBundles = await databaseContext.Database.SqlQuery<string>($"""
            select barcode as "Value" from promo.bundles
            where company_id = {companyId} and not is_deleted and barcode = any({barcodeArray})
            """).ToListAsync(cancellationToken);
        return usedByVariants.Concat(usedByBundles).Distinct(StringComparer.Ordinal).ToList();
    }

    private static void ApplyHeader(Product product, ProductHeaderData header)
    {
        product.Name = header.Name;
        product.NameUrdu = header.NameUrdu;
        product.DepartmentId = header.DepartmentId;
        product.CategoryId = header.CategoryId;
        product.BrandId = header.BrandId;
        product.SeasonId = header.SeasonId;
        product.GenderId = header.GenderId;
        product.ProductTypeId = header.ProductTypeId;
        product.UnitId = header.UnitId;
        product.LocationId = header.LocationId;
        product.Weight = header.Weight;
        product.Description = header.Description;
        product.TaxRate = header.TaxRate;
        product.TaxCode = header.TaxCode;
        product.Status = header.Status;
    }
}
