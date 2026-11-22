namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>
/// The pricing engine's output (promo.effective_prices). The engine is rebuilt by the application after any price or discount change; the
/// application role cannot call promo.rebuild_effective_prices directly, so a SECURITY DEFINER wrapper for the session's company does it (migration 0300).
/// Implemented in EFootwear.DAL/Catalogue/Repositories/PricingEngineRepository.cs.
/// </summary>
public interface IPricingEngineRepository
{
    Task RefreshEffectivePricesAsync(CancellationToken cancellationToken);

    /// <summary>How many sizes of these products are sold at the store price because two discounts overlap (null products = the whole company).</summary>
    Task<int> CountVariantsInConflictAsync(IReadOnlyCollection<Guid>? productIds, CancellationToken cancellationToken);
}
