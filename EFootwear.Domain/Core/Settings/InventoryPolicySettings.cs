namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// Inventory policies (Part 7 and 10). The policy values are [BUSINESS DECISION REQUIRED]: null means "not configured" and the
/// modules that post stock must not invent a rule; they enforce only what the database already enforces.
/// </summary>
public sealed record InventoryPolicySettings(
    bool? AllowNegativeStock,
    bool? AllowBackdatedStockAdjustments,
    int? BackdatedStockAdjustmentMaximumDays);
