using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Runs the pricing engine (promo.rebuild_effective_prices) through the SECURITY DEFINER wrapper of migration 0300 and counts overlapping discounts.
/// Pending EF Core changes are saved first, inside the same transaction, because the engine reads the prices and rules from the database.
/// </summary>
public sealed class PricingEngineRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPricingEngineRepository
{
    public async Task RefreshEffectivePricesAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        await databaseContext.Database.SqlQuery<long>($"select promo.refresh_effective_prices_for_current_company() as \"Value\"").SingleAsync(cancellationToken);
    }

    public async Task<int> CountVariantsInConflictAsync(IReadOnlyCollection<Guid>? productIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        if (productIds is null)
            return (int)await databaseContext.Database.SqlQuery<long>($"""
                select count(distinct ep.variant_id) as "Value" from promo.effective_prices ep
                where ep.company_id = {companyId} and ep.is_conflict
                """).SingleAsync(cancellationToken);

        var productArray = productIds.ToArray();
        return (int)await databaseContext.Database.SqlQuery<long>($"""
            select count(distinct ep.variant_id) as "Value" from promo.effective_prices ep
            join catalog.variants v on v.company_id = ep.company_id and v.id = ep.variant_id
            where ep.company_id = {companyId} and ep.is_conflict and v.product_id = any({productArray})
            """).SingleAsync(cancellationToken);
    }
}
