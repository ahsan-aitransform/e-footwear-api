using EFootwear.BLL.Promotions.Contracts;

namespace EFootwear.BLL.Promotions.Abstractions;

/// <summary>
/// Special offers, sales on stores and sales on categories (promo.rules with created_from of the screen) and the products of an offer (promo.rule_targets).
/// Every method names the kind, so a screen can only read and change its own rules. Implemented in the DAL. The prices these rules lead to are made by the engine
/// (promo.rebuild_effective_prices), which the services run after every change through <see cref="Catalogue.Abstractions.IPricingEngineRepository"/>.
/// </summary>
public interface IPromotionRuleRepository
{
    /// <summary>The not deleted rules of the kind, newest first. Stores and categories are not loaded; the counts are.</summary>
    Task<IReadOnlyList<PromotionRuleResponse>> ListAsync(string kindCode, CancellationToken cancellationToken);

    /// <summary>One rule with its stores and categories, or null.</summary>
    Task<PromotionRuleResponse?> FindAsync(string kindCode, Guid ruleId, CancellationToken cancellationToken);

    /// <summary>Every category that is not deleted, with its department, for the category picker of a sale (docx 2.16 "Department -- Category").</summary>
    Task<IReadOnlyList<PromotionCategoryResponse>> ListCategoryOptionsAsync(CancellationToken cancellationToken);

    Task<int> CountExistingCategoriesAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <summary>Creates the rule with its stores, its channel and its categories. Returns the new id, or null when a sale needs the point of sale channel and the company has none.</summary>
    Task<Guid?> CreateAsync(string kindCode, SavePromotionRuleRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the settings, stores and categories when the row version is still the one given. False means no such rule with that version.</summary>
    Task<bool> UpdateAsync(string kindCode, Guid ruleId, SavePromotionRuleRequest request, long expectedRowVersion, Guid userId, CancellationToken cancellationToken);

    /// <summary>Marks the rule deleted and cancelled. It stays in the database, so an invoice that used it can still name it. False means no such rule.</summary>
    Task<bool> DeleteAsync(string kindCode, Guid ruleId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpecialOfferLineResponse>> ListLinesAsync(Guid ruleId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the sizes found by the barcode, the category or the brand to the offer at the percentage. Returns how many sizes matched and how many of them were added
    /// (a size already in the offer is skipped).
    /// </summary>
    Task<(int MatchedCount, int AddedCount)> AddLinesAsync(Guid ruleId, string? barcode, Guid? categoryId, Guid? brandId, decimal percentage, CancellationToken cancellationToken);

    Task<bool> UpdateLineAsync(Guid ruleId, Guid lineId, decimal percentage, CancellationToken cancellationToken);

    Task<bool> DeleteLineAsync(Guid ruleId, Guid lineId, CancellationToken cancellationToken);
}
