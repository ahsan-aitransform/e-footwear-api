namespace EFootwear.Domain.Inventory;

/// <summary>The kinds of inv.document_extras (column kind): how a stock adjustment came about.</summary>
public static class StockAdjustmentKindCodes
{
    /// <summary>Typed on the Stock Adjustment screen (docx 5.7): add or remove pieces.</summary>
    public const string Manual = "MANUAL";

    /// <summary>The counted stock typed into the non-zero stock list (docx 5.8): the difference to the stock shown is posted.</summary>
    public const string CountUpdate = "COUNT_UPDATE";

    /// <summary>A void: a new document that undoes an earlier adjustment.</summary>
    public const string Reversal = "REVERSAL";

    /// <summary>The differences of a stock audit posted with "Adjust Stock".</summary>
    public const string AuditAdjustment = "AUDIT_ADJUSTMENT";

    public static bool IsValid(string? code) => code is Manual or CountUpdate or Reversal or AuditAdjustment;
}
