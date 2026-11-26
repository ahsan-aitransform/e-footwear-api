using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Promotions.Repositories;

/// <summary>
/// Open promotion conflicts (promo.conflict_groups with promo.conflict_candidates, both made by the pricing engine) and their resolution. A group is resolved by setting its status and the
/// chosen promotion; the engine keeps a resolved group while the same promotions cover the size and opens a new one when they change. Only the stores the caller may use are listed.
/// </summary>
public sealed class PromotionConflictRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPromotionConflictRepository
{
    public async Task<PagedResult<PromotionConflictResponse>> ListOpenAsync(Guid? storeId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var groupRows = await databaseContext.Database.SqlQueryRaw<GroupRow>("""
            select count(*) over() as "TotalCount", g.id as "GroupId", g.store_id as "StoreId", s.name as "StoreName", g.variant_id as "VariantId",
                   v.barcode as "Barcode", p.name as "ProductName", v.size_code as "SizeCode", coalesce(ep.list_price, 0) as "ListPrice"
              from promo.conflict_groups g
              join core.stores s on s.company_id = g.company_id and s.id = g.store_id
              join catalog.variants v on v.company_id = g.company_id and v.id = g.variant_id
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
              left join promo.effective_prices ep on ep.company_id = g.company_id and ep.channel_id = g.channel_id and ep.store_id = g.store_id and ep.variant_id = g.variant_id
             where g.company_id = {0} and g.status = 'OPEN'
               and (cast({1} as uuid) is null or g.store_id = cast({1} as uuid))
               and (cast({2} as boolean) or g.store_id = any(cast({3} as uuid[])))
             order by p.name, case when v.size_code ~ '^[0-9]+(\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code, g.id
             limit {4} offset {5}
            """, companyId, storeId, currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(), pageRequest.PageSize, pageRequest.SkippedItemCount)
            .ToListAsync(cancellationToken);
        if (groupRows.Count == 0) return PagedResult<PromotionConflictResponse>.Empty(pageRequest);

        var groupIds = groupRows.Select(row => row.GroupId).ToArray();
        var candidateRows = await databaseContext.Database.SqlQuery<CandidateRow>($"""
            select cc.group_id as "GroupId", cc.rule_id as "RuleId", r.name as "RuleName", r.created_from as "KindCode", cc.sale_price as "SalePrice",
                   cc.discount_percent as "DiscountPercent"
              from promo.conflict_candidates cc
              join promo.rules r on r.company_id = cc.company_id and r.id = cc.rule_id
             where cc.company_id = {companyId} and cc.group_id = any({groupIds})
             order by cc.sale_price, r.name, cc.rule_id
            """).ToListAsync(cancellationToken);
        var candidatesByGroup = candidateRows.ToLookup(row => row.GroupId);

        var items = groupRows.Select(row => new PromotionConflictResponse(
            row.GroupId, row.StoreId, row.StoreName, row.VariantId, row.Barcode, row.ProductName, row.SizeCode, row.ListPrice,
            candidatesByGroup[row.GroupId].Select(candidate => new PromotionConflictCandidateResponse(candidate.RuleId, candidate.RuleName, candidate.KindCode, candidate.SalePrice, candidate.DiscountPercent)).ToList())).ToList();
        return new PagedResult<PromotionConflictResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, groupRows[0].TotalCount);
    }

    public async Task<IReadOnlyList<Guid>?> FindOpenGroupCandidateIdsAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var isOpen = await databaseContext.Database.SqlQuery<bool>($"""
            select exists (select 1 from promo.conflict_groups g where g.company_id = {companyId} and g.id = {groupId} and g.status = 'OPEN') as "Value"
            """).SingleAsync(cancellationToken);
        if (!isOpen) return null;
        return await databaseContext.Database.SqlQuery<Guid>($"""
            select cc.rule_id as "Value" from promo.conflict_candidates cc where cc.company_id = {companyId} and cc.group_id = {groupId}
            """).ToListAsync(cancellationToken);
    }

    public async Task<bool> ResolveAsync(Guid groupId, Guid chosenRuleId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.conflict_groups
               set status = 'RESOLVED', chosen_rule_id = {chosenRuleId}, resolved_by = {userId}, resolved_at = now()
             where company_id = {companyId} and id = {groupId} and status = 'OPEN'
            """, cancellationToken);
        return affectedRows > 0;
    }

    private sealed class GroupRow
    {
        public long TotalCount { get; set; }
        public Guid GroupId { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public Guid VariantId { get; set; }
        public string Barcode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public long ListPrice { get; set; }
    }

    private sealed class CandidateRow
    {
        public Guid GroupId { get; set; }
        public Guid RuleId { get; set; }
        public string RuleName { get; set; } = "";
        public string KindCode { get; set; } = "";
        public long SalePrice { get; set; }
        public decimal? DiscountPercent { get; set; }
    }
}
