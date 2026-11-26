using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Commissions.Repositories;

/// <summary>
/// Commission plans, members and rules. Plain SQL over hr.commission_plans, hr.commission_plan_members and hr.commission_rules; the tables are isolated by
/// company, and every change is written to the audit trail by the database. A rule is never edited: it is ended, so the rate of a past day stays provable.
/// Replacing the members of a plan needs DELETE on hr.commission_plan_members (migration 0850).
/// </summary>
public sealed class CommissionPlanRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICommissionPlanRepository
{
    public async Task<IReadOnlyList<CommissionPlanResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<PlanRow>($"""
            select p.id as "Id", p.name as "Name", p.applies_to as "AppliesToCode", p.basis as "BasisCode", p.calc_type as "CalculationTypeCode",
                   p.default_rate as "DefaultRate", p.default_fixed as "DefaultFixedAmountPerPiece", p.min_store_sales as "MinimumStoreSales",
                   p.store_id as "StoreId", s.name as "StoreName", p.valid_from as "ValidFrom", p.valid_to as "ValidTo", p.is_active as "IsActive",
                   cast((select count(*) from hr.commission_plan_members m where m.company_id = p.company_id and m.plan_id = p.id) as int) as "MemberCount",
                   cast((select count(*) from hr.commission_rules r where r.company_id = p.company_id and r.plan_id = p.id) as int) as "RuleCount",
                   p.row_ver as "RowVersion"
              from hr.commission_plans p
              left join core.stores s on s.company_id = p.company_id and s.id = p.store_id
             where p.company_id = {companyId} and not p.is_deleted
             order by p.is_active desc, p.valid_from desc, p.name, p.id
            """).ToListAsync(cancellationToken);
        return rows.Select(row => ToResponse(row, [], [])).ToList();
    }

    public async Task<CommissionPlanResponse?> FindAsync(Guid planId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await databaseContext.Database.SqlQuery<PlanRow>($"""
            select p.id as "Id", p.name as "Name", p.applies_to as "AppliesToCode", p.basis as "BasisCode", p.calc_type as "CalculationTypeCode",
                   p.default_rate as "DefaultRate", p.default_fixed as "DefaultFixedAmountPerPiece", p.min_store_sales as "MinimumStoreSales",
                   p.store_id as "StoreId", s.name as "StoreName", p.valid_from as "ValidFrom", p.valid_to as "ValidTo", p.is_active as "IsActive",
                   0 as "MemberCount", 0 as "RuleCount", p.row_ver as "RowVersion"
              from hr.commission_plans p
              left join core.stores s on s.company_id = p.company_id and s.id = p.store_id
             where p.company_id = {companyId} and p.id = {planId} and not p.is_deleted
            """).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var memberRows = await databaseContext.Database.SqlQuery<MemberRow>($"""
            select m.staff_id as "StaffId", st.code as "StaffCode", st.name as "StaffName"
              from hr.commission_plan_members m
              join contacts.staff st on st.company_id = m.company_id and st.id = m.staff_id
             where m.company_id = {companyId} and m.plan_id = {planId}
             order by st.name, st.id
            """).ToListAsync(cancellationToken);

        var ruleRows = await databaseContext.Database.SqlQuery<RuleRow>($"""
            select r.id as "Id", r.rule_scope as "ScopeCode", r.product_id as "ProductId", pr.name as "ProductName",
                   r.category_id as "CategoryId", c.name as "CategoryName", r.price_band_id as "PriceBandId", pb.name as "PriceBandName",
                   r.calc_type as "CalculationTypeCode", r.rate as "Rate", r.fixed_amount as "FixedAmountPerPiece",
                   r.min_line_amount as "MinimumLineAmount", r.priority as "Priority", r.valid_from as "ValidFrom", r.valid_to as "ValidTo"
              from hr.commission_rules r
              left join catalog.products pr on pr.company_id = r.company_id and pr.id = r.product_id
              left join master.categories c on c.company_id = r.company_id and c.id = r.category_id
              left join catalog.price_bands pb on pb.company_id = r.company_id and pb.id = r.price_band_id
             where r.company_id = {companyId} and r.plan_id = {planId}
             order by r.valid_from desc, r.rule_scope, r.id
            """).ToListAsync(cancellationToken);

        return ToResponse(row,
            memberRows.Select(member => new CommissionPlanMemberResponse(member.StaffId, member.StaffCode, member.StaffName)).ToList(),
            ruleRows.Select(rule => new CommissionRuleResponse(
                rule.Id, rule.ScopeCode, rule.ProductId, rule.ProductName, rule.CategoryId, rule.CategoryName, rule.PriceBandId, rule.PriceBandName,
                rule.CalculationTypeCode, rule.Rate, rule.FixedAmountPerPiece, rule.MinimumLineAmount, rule.Priority, rule.ValidFrom, rule.ValidTo)).ToList());
    }

    public async Task<int> CountExistingStaffAsync(IReadOnlyCollection<Guid> staffIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var idArray = staffIds.ToArray();
        var count = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value" from contacts.staff st
             where st.company_id = {companyId} and not st.is_deleted and st.id = any({idArray})
            """).SingleAsync(cancellationToken);
        return (int)count;
    }

    public async Task<Guid> CreateAsync(SaveCommissionPlanRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        // INSERT ... RETURNING cannot be wrapped in a sub-select, so the rows are read with ToListAsync (no LINQ operator is added to the SQL).
        var insertedIds = await databaseContext.Database.SqlQuery<Guid>($"""
            insert into hr.commission_plans (company_id, name, applies_to, basis, calc_type, default_rate, default_fixed, min_store_sales,
                                             store_id, valid_from, valid_to, is_active, created_by)
            values ({companyId}, {request.Name}, {request.AppliesToCode}, {request.BasisCode}, {request.CalculationTypeCode}, {request.DefaultRate},
                    {request.DefaultFixedAmountPerPiece}, {request.MinimumStoreSales}, {request.StoreId}, {request.ValidFrom}, {request.ValidTo},
                    {request.IsActive}, {userId})
            returning id as "Value"
            """).ToListAsync(cancellationToken);
        var planId = insertedIds.Single();
        await InsertMembersAsync(planId, request.MemberStaffIds ?? [], cancellationToken);
        return planId;
    }

    public async Task<bool> UpdateAsync(Guid planId, SaveCommissionPlanRequest request, long expectedRowVersion, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update hr.commission_plans
               set name = {request.Name}, applies_to = {request.AppliesToCode}, basis = {request.BasisCode}, calc_type = {request.CalculationTypeCode},
                   default_rate = {request.DefaultRate}, default_fixed = {request.DefaultFixedAmountPerPiece}, min_store_sales = {request.MinimumStoreSales},
                   store_id = {request.StoreId}, valid_from = {request.ValidFrom}, valid_to = {request.ValidTo}, is_active = {request.IsActive},
                   updated_by = {userId}
             where company_id = {companyId} and id = {planId} and not is_deleted and row_ver = {expectedRowVersion}
            """, cancellationToken);
        if (affectedRows == 0) return false;

        await databaseContext.Database.ExecuteSqlAsync($"""
            delete from hr.commission_plan_members where company_id = {companyId} and plan_id = {planId}
            """, cancellationToken);
        await InsertMembersAsync(planId, request.MemberStaffIds ?? [], cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid planId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update hr.commission_plans set is_deleted = true, is_active = false, updated_by = {userId}
             where company_id = {companyId} and id = {planId} and not is_deleted
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<Guid?> AddRuleAsync(Guid planId, SaveCommissionRuleRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        try
        {
            var insertedIds = await databaseContext.Database.SqlQuery<Guid>($"""
                insert into hr.commission_rules (company_id, plan_id, rule_scope, product_id, category_id, price_band_id, calc_type, rate, fixed_amount,
                                                 min_line_amount, priority, valid_from, valid_to, created_by)
                values ({companyId}, {planId}, {request.ScopeCode}, {request.ProductId}, {request.CategoryId}, {request.PriceBandId}, {request.CalculationTypeCode},
                        {request.Rate}, {request.FixedAmountPerPiece}, {request.MinimumLineAmount}, {request.Priority}, {request.ValidFrom}, {request.ValidTo}, {userId})
                returning id as "Value"
                """).ToListAsync(cancellationToken);
            return insertedIds.Single();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            return null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new DomainException(ErrorKind.Validation, "The product, category or price band of the rule does not exist.");
        }
    }

    public async Task<bool> EndRuleAsync(Guid planId, Guid ruleId, DateOnly validTo, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update hr.commission_rules set valid_to = {validTo}, updated_by = {userId}
             where company_id = {companyId} and plan_id = {planId} and id = {ruleId} and (valid_to is null or valid_to > {validTo})
            """, cancellationToken);
        return affectedRows > 0;
    }

    public async Task<DateOnly?> FindRuleFirstDayAsync(Guid planId, Guid ruleId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var days = await databaseContext.Database.SqlQuery<DateOnly>($"""
            select r.valid_from as "Value" from hr.commission_rules r
             where r.company_id = {companyId} and r.plan_id = {planId} and r.id = {ruleId}
            """).ToListAsync(cancellationToken);
        return days.Count == 0 ? null : days[0];
    }

    private async Task InsertMembersAsync(Guid planId, IReadOnlyList<Guid> staffIds, CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0) return;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var idArray = staffIds.ToArray();
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into hr.commission_plan_members (company_id, plan_id, staff_id)
            select {companyId}, {planId}, member_id from unnest({idArray}) as member_id
            """, cancellationToken);
    }

    private static CommissionPlanResponse ToResponse(PlanRow row, IReadOnlyList<CommissionPlanMemberResponse> members, IReadOnlyList<CommissionRuleResponse> rules) => new(
        row.Id, row.Name, row.AppliesToCode, row.BasisCode, row.CalculationTypeCode, row.DefaultRate, row.DefaultFixedAmountPerPiece, row.MinimumStoreSales,
        row.StoreId, row.StoreName, row.ValidFrom, row.ValidTo, row.IsActive,
        members.Count > 0 ? members.Count : row.MemberCount, rules.Count > 0 ? rules.Count : row.RuleCount, members, rules, row.RowVersion);

    private sealed class PlanRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string AppliesToCode { get; set; } = "";
        public string BasisCode { get; set; } = "";
        public string CalculationTypeCode { get; set; } = "";
        public decimal? DefaultRate { get; set; }
        public long? DefaultFixedAmountPerPiece { get; set; }
        public long MinimumStoreSales { get; set; }
        public Guid? StoreId { get; set; }
        public string? StoreName { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
        public bool IsActive { get; set; }
        public int MemberCount { get; set; }
        public int RuleCount { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class MemberRow
    {
        public Guid StaffId { get; set; }
        public string StaffCode { get; set; } = "";
        public string StaffName { get; set; } = "";
    }

    private sealed class RuleRow
    {
        public Guid Id { get; set; }
        public string ScopeCode { get; set; } = "";
        public Guid? ProductId { get; set; }
        public string? ProductName { get; set; }
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public Guid? PriceBandId { get; set; }
        public string? PriceBandName { get; set; }
        public string CalculationTypeCode { get; set; } = "";
        public decimal? Rate { get; set; }
        public long? FixedAmountPerPiece { get; set; }
        public long MinimumLineAmount { get; set; }
        public int Priority { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }
}
