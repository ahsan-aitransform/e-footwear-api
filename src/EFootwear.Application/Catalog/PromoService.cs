using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Catalog;

public sealed record RuleTargetBody(long? CategoryId, long? ProductId, long? VariantId, decimal? DiscountOverride);

public sealed record RuleBody(string? Name, string? DiscountType, decimal DiscountValue, string? TargetType, decimal? StockMin, decimal? StockMax,
                              DateTimeOffset StartsAt, DateTimeOffset? EndsAt, int Priority, List<RuleTargetBody>? Targets, long[]? StoreIds, long[]? ChannelIds,
                              bool Publish = false, long? RowVer = null);

public sealed record BundleItemBody(long VariantId, decimal Qty, long NetUnitPrice);
public sealed record BundleBody(string? Name, string? Barcode, long BundlePrice, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, List<BundleItemBody>? Items, bool Publish = false);
public sealed record ResolveBody(long RuleId);

/// <summary>
/// Promotions: rules (percent / amount off / fixed price on everything, a category, an article or a size, optionally per store, channel and
/// stock level), deal bundles (one barcode, several articles) and conflicts. The database engine decides the price; an overlap of
/// two rules is never resolved silently, it is held as a conflict until an administrator chooses (Part 18).
/// </summary>
public sealed class PromoService(IUnitOfWorkFactory uows, PriceRefresher prices)
{
    public async Task<Page<Row>> RulesAsync(UserContext u, string? status, PageQuery q, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.RulesAsync(status, q, ct);
    }

    public async Task<Row> RuleAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.RuleAsync(id, ct) ?? throw new NotFoundException("Promotion", id);
    }

    public async Task<long> CreateRuleAsync(UserContext u, RuleBody b, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        var input = Validate(u, b);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Promos.InsertRuleAsync(u.CompanyId, input, ct);
        await uow.Promos.ReplaceRuleScopeAsync(u.CompanyId, id, Targets(b), b.StoreIds ?? [], b.ChannelIds ?? [], ct);
        if (b.Publish) await uow.Promos.SetRuleStatusAsync(id, "PUBLISHED", ct);
        await uow.CommitAsync(ct);
        if (b.Publish) await prices.RebuildAsync(u.CompanyId, ct);
        return id;
    }

    public async Task UpdateRuleAsync(UserContext u, long id, RuleBody b, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        var input = Validate(u, b);
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Promos.RuleAsync(id, ct) ?? throw new NotFoundException("Promotion", id);
        if (cur.Get<string>("status") == "CANCELLED") throw new BusinessRuleException("A cancelled promotion cannot be changed. Create a new one.");
        if (!await uow.Promos.UpdateRuleAsync(id, input, b.RowVer, ct)) throw new ConflictException("This promotion was changed by someone else. Reload and try again.", "stale");
        await uow.Promos.ReplaceRuleScopeAsync(u.CompanyId, id, Targets(b), b.StoreIds ?? [], b.ChannelIds ?? [], ct);
        await uow.CommitAsync(ct);
        if (cur.Get<string>("status") == "PUBLISHED") await prices.RebuildAsync(u.CompanyId, ct);
    }

    public async Task PublishAsync(UserContext u, long id, CancellationToken ct) => await SetStatusAsync(u, id, "PUBLISHED", ct);
    public async Task CancelAsync(UserContext u, long id, CancellationToken ct) => await SetStatusAsync(u, id, "CANCELLED", ct);

    private async Task SetStatusAsync(UserContext u, long id, string status, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Promos.RuleAsync(id, ct) ?? throw new NotFoundException("Promotion", id);
        var from = cur.Get<string>("status");
        if (from == "CANCELLED") throw new BusinessRuleException("A cancelled promotion cannot be reopened.");
        if (status == "PUBLISHED" && from == "PUBLISHED") return;
        await uow.Promos.SetRuleStatusAsync(id, status, ct);
        await uow.CommitAsync(ct);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    public async Task DeleteRuleAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Promos.RuleAsync(id, ct) is null) throw new NotFoundException("Promotion", id);
        await uow.Promos.SoftDeleteRuleAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    /// <summary>Re-derive every counter price now (the background worker does the same on a timer).</summary>
    public async Task RefreshAsync(UserContext u, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    public async Task<List<Row>> ChannelsAsync(UserContext u, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.ChannelsAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ conflicts

    public async Task<List<Row>> ConflictsAsync(UserContext u, CancellationToken ct)
    {
        u.RequireAny(P.PromoConflictResolve, P.PromoManage);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.ConflictsAsync(ct);
    }

    public async Task ResolveConflictAsync(UserContext u, long id, ResolveBody b, CancellationToken ct)
    {
        u.Require(P.PromoConflictResolve);
        await using var uow = await uows.BeginAsync(u, ct);
        var g = await uow.Promos.ConflictAsync(id, ct) ?? throw new NotFoundException("Conflict", id);
        if (g.Get<string>("status") != "OPEN") throw new BusinessRuleException("This conflict is already settled or no longer applies.", "not_open");
        var cands = g["candidateRules"] as long[] ?? [];
        if (!cands.Contains(b.RuleId)) throw new ValidationException("ruleId", "Choose one of the promotions in conflict.");
        await uow.Promos.ResolveConflictAsync(id, b.RuleId, u.UserId, ct);
        await uow.CommitAsync(ct);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    // ------------------------------------------------------------------------------------------ bundles

    public async Task<Page<Row>> BundlesAsync(UserContext u, PageQuery q, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.BundlesAsync(q, ct);
    }

    public async Task<Row> BundleAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Promos.BundleAsync(id, ct) ?? throw new NotFoundException("Deal", id);
    }

    public async Task<long> CreateBundleAsync(UserContext u, BundleBody b, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        ValidateBundle(b);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Promos.InsertBundleAsync(u.CompanyId, b.Name!.Trim(), b.Barcode!.Trim().ToUpperInvariant(), b.BundlePrice, b.StartsAt, b.EndsAt, ct);
        await uow.Promos.ReplaceBundleItemsAsync(u.CompanyId, id, b.Items!.Select(i => new BundleItemInput(i.VariantId, i.Qty, i.NetUnitPrice)).ToList(), ct);
        if (b.Publish) await uow.Promos.SetBundleStatusAsync(id, "PUBLISHED", ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task SetBundleStatusAsync(UserContext u, long id, string status, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        if (status is not ("PUBLISHED" or "CANCELLED")) throw new ValidationException("status", "Status must be PUBLISHED or CANCELLED.");
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Promos.BundleAsync(id, ct) is null) throw new NotFoundException("Deal", id);
        await uow.Promos.SetBundleStatusAsync(id, status, ct);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteBundleAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.PromoManage);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Promos.BundleAsync(id, ct) is null) throw new NotFoundException("Deal", id);
        await uow.Promos.SoftDeleteBundleAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static RuleInput Validate(UserContext u, RuleBody b)
    {
        var c = new Check().NotBlank(b.Name, "name")
            .Require(b.DiscountType is "PERCENT" or "AMOUNT" or "FIXED_PRICE", "discountType", "Discount type must be PERCENT, AMOUNT or FIXED_PRICE.")
            .Require(b.TargetType is "ALL" or "CATEGORY" or "PRODUCT" or "VARIANT", "targetType", "Target must be ALL, CATEGORY, PRODUCT or VARIANT.")
            .Require(b.DiscountValue >= 0, "discountValue", "Discount cannot be negative.")
            .Require(b.DiscountType != "PERCENT" || b.DiscountValue <= 100, "discountValue", "A percentage cannot exceed 100.")
            .Require(b.EndsAt is null || b.EndsAt > b.StartsAt, "endsAt", "The end must be after the start.")
            .Require(b.StockMin is null || b.StockMax is null || b.StockMax >= b.StockMin, "stockMax", "Maximum stock must not be below minimum stock.");
        var targets = b.Targets ?? [];
        c.Require(b.TargetType == "ALL" ? targets.Count == 0 : targets.Count > 0, "targets", b.TargetType == "ALL" ? "A promotion on everything has no targets." : "Choose at least one target.");
        c.Require(targets.All(t => b.TargetType switch { "CATEGORY" => t.CategoryId is not null && t.ProductId is null && t.VariantId is null,
                                                         "PRODUCT" => t.ProductId is not null && t.CategoryId is null && t.VariantId is null,
                                                         "VARIANT" => t.VariantId is not null && t.CategoryId is null && t.ProductId is null, _ => true }),
                  "targets", "Each target must match the promotion's target type.");
        c.ThrowIfAny();
        foreach (var s in b.StoreIds ?? []) u.RequireStore(s);
        return new RuleInput(b.Name!.Trim(), b.DiscountType!, b.DiscountValue, b.TargetType!, b.StockMin, b.StockMax, b.StartsAt, b.EndsAt, b.Priority);
    }

    private static List<RuleTarget> Targets(RuleBody b)
        => (b.Targets ?? []).Select(t => new RuleTarget(t.CategoryId, t.ProductId, t.VariantId, t.DiscountOverride)).ToList();

    private static void ValidateBundle(BundleBody b)
    {
        var c = new Check().NotBlank(b.Name, "name").NotBlank(b.Barcode, "barcode").Require(b.BundlePrice >= 0, "bundlePrice", "Price cannot be negative.")
            .Require(b.EndsAt is null || b.EndsAt > b.StartsAt, "endsAt", "The end must be after the start.")
            .Require(b.Items is { Count: > 0 }, "items", "A deal needs at least one article.");
        if (b.Items is not null)
        {
            c.Require(b.Items.All(i => i.Qty > 0 && i.NetUnitPrice >= 0), "items", "Each article needs a positive quantity and a price of zero or more.");
            c.Require(b.Items.Select(i => i.VariantId).Distinct().Count() == b.Items.Count, "items", "The same size appears twice.");
            c.Require(b.Items.Sum(i => (decimal)i.NetUnitPrice * i.Qty) == b.BundlePrice, "bundlePrice", "The article prices must add up to the deal price.");
        }
        c.ThrowIfAny();
    }
}
