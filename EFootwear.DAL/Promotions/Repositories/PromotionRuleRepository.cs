using EFootwear.DAL.Common;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Promotions.Repositories;

/// <summary>
/// Special offers, sales on stores and sales on categories as promotion rules (promo.rules, rule_stores, rule_channels, rule_targets), and the products of an offer.
/// Every statement names the kind (created_from) so a screen reaches only its own rules, and the deleted flag so a deleted rule never reappears. A rule that names stores must
/// name the in-store point of sale channel (the database requires it); an offer with no store names no channel and so also covers the online channel. Rows are inserted in separate
/// statements because the target trigger reads the rule row. The table of the prices is made by the engine, never written here.
/// </summary>
public sealed class PromotionRuleRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPromotionRuleRepository
{
    private const string StorePointOfSaleChannelKind = "STORE_POS";

    // Where a rule stands today; the same words as PromotionPhaseCodes. A deleted rule is never selected.
    private const string PhaseSql = """
        case when r.status = 'DRAFT' then 'INACTIVE'
             when r.starts_at > now() then 'SCHEDULED'
             when r.ends_at is not null and r.ends_at <= now() then 'ENDED'
             else 'RUNNING' end
        """;

    // Parameters: 0 company, 1 kind.
    private const string RuleColumns = $$"""
        r.id as "Id", r.name as "Name", r.discount_value as "DiscountValue", r.starts_at as "StartsAt", r.ends_at as "EndsAt", r.status as "StatusCode",
        {{PhaseSql}} as "PhaseCode",
        cast((select count(*) from promo.rule_stores s where s.company_id = r.company_id and s.rule_id = r.id) as int) as "StoreCount",
        cast((select count(*) from promo.rule_targets t where t.company_id = r.company_id and t.rule_id = r.id and t.category_id is not null) as int) as "CategoryCount",
        cast((select count(*) from promo.rule_targets t where t.company_id = r.company_id and t.rule_id = r.id and t.variant_id is not null) as int) as "LineCount",
        r.row_ver as "RowVersion"
        """;

    public async Task<IReadOnlyList<PromotionRuleResponse>> ListAsync(string kindCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<RuleRow>($$"""
            select {{RuleColumns}}
              from promo.rules r
             where r.company_id = {0} and r.created_from = {1} and not r.is_deleted
             order by r.starts_at desc, r.name, r.id
            """, currentUser.CompanyId, kindCode).ToListAsync(cancellationToken);
        return rows.Select(row => ToResponse(kindCode, row, [], [])).ToList();
    }

    public async Task<PromotionRuleResponse?> FindAsync(string kindCode, Guid ruleId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await databaseContext.Database.SqlQueryRaw<RuleRow>($$"""
            select {{RuleColumns}}
              from promo.rules r
             where r.company_id = {0} and r.created_from = {1} and r.id = {2} and not r.is_deleted
            """, companyId, kindCode, ruleId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var stores = await databaseContext.Database.SqlQuery<StoreRow>($"""
            select s.id as "StoreId", s.name as "StoreName"
              from promo.rule_stores rs
              join core.stores s on s.company_id = rs.company_id and s.id = rs.store_id
             where rs.company_id = {companyId} and rs.rule_id = {ruleId}
             order by s.name, s.id
            """).ToListAsync(cancellationToken);
        var categories = await databaseContext.Database.SqlQuery<CategoryRow>($"""
            select c.id as "CategoryId", c.name as "CategoryName", d.name as "DepartmentName"
              from promo.rule_targets t
              join master.categories c on c.company_id = t.company_id and c.id = t.category_id
              join master.departments d on d.company_id = c.company_id and d.id = c.department_id
             where t.company_id = {companyId} and t.rule_id = {ruleId} and t.category_id is not null
             order by d.name, c.name, c.id
            """).ToListAsync(cancellationToken);

        return ToResponse(kindCode, row,
            stores.Select(store => new PromotionStoreResponse(store.StoreId, store.StoreName)).ToList(),
            categories.Select(category => new PromotionCategoryResponse(category.CategoryId, category.CategoryName, category.DepartmentName)).ToList());
    }

    public async Task<IReadOnlyList<PromotionCategoryResponse>> ListCategoryOptionsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<CategoryRow>($"""
            select c.id as "CategoryId", c.name as "CategoryName", d.name as "DepartmentName"
              from master.categories c
              join master.departments d on d.company_id = c.company_id and d.id = c.department_id
             where c.company_id = {companyId} and not c.is_deleted and c.is_active
             order by d.name, c.name, c.id
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new PromotionCategoryResponse(row.CategoryId, row.CategoryName, row.DepartmentName)).ToList();
    }

    public async Task<int> CountExistingCategoriesAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var idArray = categoryIds.ToArray();
        return (int)await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value" from master.categories c
             where c.company_id = {companyId} and not c.is_deleted and c.id = any({idArray})
            """).SingleAsync(cancellationToken);
    }

    public async Task<Guid?> CreateAsync(string kindCode, SavePromotionRuleRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var storeIds = (request.StoreIds ?? []).ToArray();

        Guid? channelId = null;
        if (storeIds.Length > 0)
        {
            channelId = await FindStorePointOfSaleChannelIdAsync(cancellationToken);
            if (channelId is null) return null;
        }

        var discountValue = request.DiscountPercent ?? 0m;
        var targetType = TargetTypeOf(kindCode);
        var statusCode = request.IsActive ? PromotionStatusCodes.Published : PromotionStatusCodes.Draft;
        // INSERT ... RETURNING cannot be wrapped in a sub-select, so the rows are read with ToListAsync (no LINQ operator is added to the SQL).
        var insertedIds = await databaseContext.Database.SqlQuery<Guid>($"""
            insert into promo.rules (company_id, name, discount_type, discount_value, target_type, starts_at, ends_at, priority, status, created_from, created_by)
            values ({companyId}, {request.Name}, 'PERCENT', {discountValue}, {targetType}, {request.StartsAt}, {request.EndsAt}, 0, {statusCode}, {kindCode}, {userId})
            returning id as "Value"
            """).ToListAsync(cancellationToken);
        var ruleId = insertedIds.Single();

        await InsertScopeAsync(ruleId, storeIds, channelId, (request.CategoryIds ?? []).ToArray(), cancellationToken);
        return ruleId;
    }

    public async Task<bool> UpdateAsync(string kindCode, Guid ruleId, SavePromotionRuleRequest request, long expectedRowVersion, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var storeIds = (request.StoreIds ?? []).ToArray();

        Guid? channelId = null;
        if (storeIds.Length > 0)
        {
            channelId = await FindStorePointOfSaleChannelIdAsync(cancellationToken);
            if (channelId is null) return false;
        }

        var discountValue = request.DiscountPercent ?? 0m;
        var statusCode = request.IsActive ? PromotionStatusCodes.Published : PromotionStatusCodes.Draft;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.rules
               set name = {request.Name}, discount_value = {discountValue}, starts_at = {request.StartsAt}, ends_at = {request.EndsAt}, status = {statusCode}, updated_by = {userId}
             where company_id = {companyId} and id = {ruleId} and created_from = {kindCode} and not is_deleted and row_ver = {expectedRowVersion}
            """, cancellationToken);
        if (affectedRows == 0) return false;

        // The stores and channel are replaced as one set. Category targets are replaced for a sale on categories; the product lines of an offer are never touched here.
        await databaseContext.Database.ExecuteSqlAsync($"delete from promo.rule_channels where company_id = {companyId} and rule_id = {ruleId}", cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"delete from promo.rule_stores where company_id = {companyId} and rule_id = {ruleId}", cancellationToken);
        if (kindCode == PromotionKindCodes.SaleOnCategories)
            await databaseContext.Database.ExecuteSqlAsync($"delete from promo.rule_targets where company_id = {companyId} and rule_id = {ruleId} and category_id is not null", cancellationToken);

        await InsertScopeAsync(ruleId, storeIds, channelId, (request.CategoryIds ?? []).ToArray(), cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string kindCode, Guid ruleId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.rules
               set is_deleted = true, deleted_at = now(), deleted_by = {userId}, status = 'CANCELLED', updated_by = {userId}
             where company_id = {companyId} and id = {ruleId} and created_from = {kindCode} and not is_deleted
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<IReadOnlyList<SpecialOfferLineResponse>> ListLinesAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<LineRow>($"""
            select t.id as "LineId", v.id as "VariantId", v.barcode as "Barcode", p.code as "ProductCode", p.name as "ProductName", v.size_code as "SizeCode",
                   col.name as "ColorName", c.name as "CategoryName", g.name as "GenderName", se.name as "SeasonName", b.name as "BrandName",
                   coalesce(t.discount_override, 0) as "DiscountPercent"
              from promo.rule_targets t
              join catalog.variants v on v.company_id = t.company_id and v.id = t.variant_id
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
              join master.categories c on c.company_id = p.company_id and c.id = p.category_id
              left join master.colors col on col.company_id = v.company_id and col.id = v.color_id
              left join master.genders g on g.company_id = p.company_id and g.id = p.gender_id
              left join master.seasons se on se.company_id = p.company_id and se.id = p.season_id
              left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
             where t.company_id = {companyId} and t.rule_id = {ruleId} and t.variant_id is not null
             order by p.name, case when v.size_code ~ '^[0-9]+(\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code, v.id
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SpecialOfferLineResponse(row.LineId, row.VariantId, row.Barcode, row.ProductCode, row.ProductName, row.SizeCode, row.ColorName,
                                                               row.CategoryName, row.GenderName, row.SeasonName, row.BrandName, row.DiscountPercent)).ToList();
    }

    public async Task<(int MatchedCount, int AddedCount)> AddLinesAsync(
        Guid ruleId, string? barcode, Guid? categoryId, Guid? brandId, decimal percentage, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        string filterColumn;
        object filterValue;
        if (barcode is not null)
        {
            // one size by its barcode; a product code adds every size of the product
            var storedBarcode = await BarcodeMatching.FindStoredBarcodeAsync(databaseContext, companyId, barcode, cancellationToken);
            var isBarcode = await databaseContext.ProductVariants.AsNoTracking()
                .AnyAsync(variant => variant.CompanyId == companyId && !variant.IsDeleted && variant.Barcode == storedBarcode, cancellationToken);
            (filterColumn, filterValue) = isBarcode ? ("v.barcode", storedBarcode) : ("lower(p.code)", barcode.ToLowerInvariant());
        }
        else (filterColumn, filterValue) = categoryId is not null ? ("p.category_id", (object)categoryId.Value) : ("p.brand_id", (object)brandId!.Value);

        // The count names only the placeholders it uses (0 company, 1 the barcode, category or brand).
        var matched = await databaseContext.Database.SqlQueryRaw<long>($$"""
            select count(*) as "Value"
              from catalog.variants v
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
             where v.company_id = {0} and not v.is_deleted and not p.is_deleted and {{filterColumn}} = {1}
            """, companyId, filterValue).SingleAsync(cancellationToken);
        if (matched == 0) return (0, 0);

        var added = await databaseContext.Database.ExecuteSqlRawAsync($$"""
            insert into promo.rule_targets (company_id, rule_id, variant_id, discount_override)
            select v.company_id, {1}, v.id, {2}
              from catalog.variants v
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
             where v.company_id = {0} and not v.is_deleted and not p.is_deleted and {{filterColumn}} = {3}
               and not exists (select 1 from promo.rule_targets t where t.company_id = v.company_id and t.rule_id = {1} and t.variant_id = v.id)
            """, [companyId, ruleId, percentage, filterValue], cancellationToken);
        return ((int)matched, added);
    }

    public async Task<bool> UpdateLineAsync(Guid ruleId, Guid lineId, decimal percentage, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.rule_targets set discount_override = {percentage}
             where company_id = {companyId} and rule_id = {ruleId} and id = {lineId} and variant_id is not null
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<bool> DeleteLineAsync(Guid ruleId, Guid lineId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            delete from promo.rule_targets where company_id = {companyId} and rule_id = {ruleId} and id = {lineId} and variant_id is not null
            """, cancellationToken);
        return affectedRows > 0;
    }

    private async Task InsertScopeAsync(Guid ruleId, Guid[] storeIds, Guid? channelId, Guid[] categoryIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        if (storeIds.Length > 0)
        {
            await databaseContext.Database.ExecuteSqlAsync($"""
                insert into promo.rule_stores (company_id, rule_id, store_id)
                select {companyId}, {ruleId}, store_id from unnest({storeIds}) as store_id
                """, cancellationToken);
            await databaseContext.Database.ExecuteSqlAsync($"""
                insert into promo.rule_channels (company_id, rule_id, channel_id) values ({companyId}, {ruleId}, {channelId})
                """, cancellationToken);
        }
        if (categoryIds.Length > 0)
        {
            await databaseContext.Database.ExecuteSqlAsync($"""
                insert into promo.rule_targets (company_id, rule_id, category_id)
                select {companyId}, {ruleId}, category_id from unnest({categoryIds}) as category_id
                """, cancellationToken);
        }
    }

    private async Task<Guid?> FindStorePointOfSaleChannelIdAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var channelIds = await databaseContext.Database.SqlQuery<Guid>($"""
            select id as "Value" from integ.channels where company_id = {companyId} and kind = {StorePointOfSaleChannelKind} and is_active order by code limit 1
            """).ToListAsync(cancellationToken);
        return channelIds.Count == 0 ? null : channelIds[0];
    }

    private static string TargetTypeOf(string kindCode) => kindCode switch
    {
        PromotionKindCodes.SaleOnStores => "ALL",
        PromotionKindCodes.SaleOnCategories => "CATEGORY",
        _ => "VARIANT",
    };

    private static PromotionRuleResponse ToResponse(string kindCode, RuleRow row, IReadOnlyList<PromotionStoreResponse> stores, IReadOnlyList<PromotionCategoryResponse> categories) => new(
        row.Id, kindCode, row.Name, kindCode == PromotionKindCodes.SpecialOffer ? null : row.DiscountValue, row.StartsAt, row.EndsAt,
        row.StatusCode == PromotionStatusCodes.Published, row.PhaseCode, stores.Count > 0 ? stores.Count : row.StoreCount,
        categories.Count > 0 ? categories.Count : row.CategoryCount, row.LineCount, stores, categories, row.RowVersion);

    private sealed class RuleRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal DiscountValue { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public string StatusCode { get; set; } = "";
        public string PhaseCode { get; set; } = "";
        public int StoreCount { get; set; }
        public int CategoryCount { get; set; }
        public int LineCount { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class StoreRow
    {
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
    }

    private sealed class CategoryRow
    {
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public string DepartmentName { get; set; } = "";
    }

    private sealed class LineRow
    {
        public Guid LineId { get; set; }
        public Guid VariantId { get; set; }
        public string Barcode { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public string CategoryName { get; set; } = "";
        public string? GenderName { get; set; }
        public string? SeasonName { get; set; }
        public string? BrandName { get; set; }
        public decimal DiscountPercent { get; set; }
    }
}
