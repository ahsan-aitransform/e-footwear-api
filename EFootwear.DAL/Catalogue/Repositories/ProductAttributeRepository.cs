using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Bulk reassignment of a product classification (docx 4.6). The column names are a fixed whitelist chosen from the field code, never text from the client.
/// A colour change touches the sizes and skips a product that already has the same size in the new colour (a size/colour pair is unique).
/// </summary>
public sealed class ProductAttributeRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductAttributeRepository
{
    private static readonly Dictionary<string, string> ProductColumnByField = new()
    {
        [ProductAttributeFieldCodes.Unit] = "unit_id",
        [ProductAttributeFieldCodes.Brand] = "brand_id",
        [ProductAttributeFieldCodes.Category] = "category_id",
        [ProductAttributeFieldCodes.Gender] = "gender_id",
        [ProductAttributeFieldCodes.Season] = "season_id",
        [ProductAttributeFieldCodes.Location] = "location_id",
        [ProductAttributeFieldCodes.ProductType] = "product_type_id",
    };

    public async Task<PagedResult<ProductAttributeRowResponse>> ListProductsWithValueAsync(string fieldType, Guid oldValueId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var oldValue = parameters.Add(oldValueId);
        var condition = fieldType == ProductAttributeFieldCodes.Color
            ? $"exists (select 1 from catalog.variants cv where cv.company_id = p.company_id and cv.product_id = p.id and not cv.is_deleted and cv.color_id = {oldValue})"
            : $"p.{ProductColumnByField[fieldType]} = {oldValue}";
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<AttributeRow>($"""
            select count(*) over() as "TotalCount", p.id as "ProductId", p.code as "ProductCode", p.name as "ProductName", u.name as "UnitName", b.name as "BrandName",
                   c.name as "CategoryName", g.name as "GenderName", s.name as "SeasonName",
                   (select string_agg(distinct col.name, ', ' order by col.name) from catalog.variants v
                      join master.colors col on col.company_id = v.company_id and col.id = v.color_id
                     where v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted) as "ColorName"
            from catalog.products p
            join master.categories c on c.company_id = p.company_id and c.id = p.category_id
            left join master.units u on u.company_id = p.company_id and u.id = p.unit_id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.genders g on g.company_id = p.company_id and g.id = p.gender_id
            left join master.seasons s on s.company_id = p.company_id and s.id = p.season_id
            where p.company_id = {company} and not p.is_deleted and {condition}
            order by p.name, p.id
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);
        var items = rows.Select(row => new ProductAttributeRowResponse(row.ProductId, row.ProductCode, row.ProductName, row.UnitName, row.BrandName,
                                                                       row.CategoryName, row.GenderName, row.SeasonName, row.ColorName)).ToList();
        return new PagedResult<ProductAttributeRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<(int ChangedCount, IReadOnlyList<string> SkippedProducts)> ApplyAsync(string fieldType, Guid oldValueId, Guid newValueId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        if (fieldType == ProductAttributeFieldCodes.Color)
            return await ApplyColorAsync(databaseContext, companyId, oldValueId, newValueId, cancellationToken);

        var parameters = new SqlParameterList();
        var company = parameters.Add(companyId);
        var oldValue = parameters.Add(oldValueId);
        var newValue = parameters.Add(newValueId);
        var column = ProductColumnByField[fieldType];
        // A category belongs to one department, so a product that moves to a category of another department moves with it.
        var departmentAssignment = fieldType == ProductAttributeFieldCodes.Category
            ? $", department_id = (select department_id from master.categories where company_id = {company} and id = {newValue})"
            : "";
        var changed = await databaseContext.Database.ExecuteSqlRawAsync($"""
            update catalog.products set {column} = {newValue}{departmentAssignment}
            where company_id = {company} and not is_deleted and {column} = {oldValue}
            """, parameters.ToArray());
        return (changed, []);
    }

    private static async Task<(int ChangedCount, IReadOnlyList<string> SkippedProducts)> ApplyColorAsync(ApplicationDatabaseContext databaseContext, Guid companyId,
                                                                                                            Guid oldColorId, Guid newColorId, CancellationToken cancellationToken)
    {
        var conflicts = await databaseContext.Database.SqlQuery<ColorConflictRow>($"""
            select distinct p.code as "ProductCode", p.name as "ProductName", v.size_code as "SizeCode"
            from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            where v.company_id = {companyId} and not v.is_deleted and v.color_id = {oldColorId}
              and exists (select 1 from catalog.variants other
                          where other.company_id = v.company_id and other.product_id = v.product_id and not other.is_deleted
                            and other.color_id = {newColorId} and other.size_code = v.size_code)
            order by p.code, v.size_code
            """).ToListAsync(cancellationToken);

        var changedProducts = await databaseContext.Database.SqlQuery<long>($"""
            select count(distinct v.product_id) as "Value" from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            where v.company_id = {companyId} and not v.is_deleted and v.color_id = {oldColorId}
              and not exists (select 1 from catalog.variants other
                              where other.company_id = v.company_id and other.product_id = v.product_id and not other.is_deleted
                                and other.color_id = {newColorId} and other.size_code = v.size_code)
            """).SingleAsync(cancellationToken);

        await databaseContext.Database.ExecuteSqlAsync($"""
            update catalog.variants v set color_id = {newColorId}
            where v.company_id = {companyId} and not v.is_deleted and v.color_id = {oldColorId}
              and not exists (select 1 from catalog.variants other
                              where other.company_id = v.company_id and other.product_id = v.product_id and not other.is_deleted
                                and other.color_id = {newColorId} and other.size_code = v.size_code)
            """, cancellationToken);

        var skipped = conflicts.Select(conflict => $"{conflict.ProductCode} {conflict.ProductName}: size {conflict.SizeCode} already exists in the new colour").ToList();
        return ((int)changedProducts, skipped);
    }

    private sealed class AttributeRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? UnitName { get; set; }
        public string? BrandName { get; set; }
        public string? CategoryName { get; set; }
        public string? GenderName { get; set; }
        public string? SeasonName { get; set; }
        public string? ColorName { get; set; }
    }

    private sealed class ColorConflictRow
    {
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
    }
}
