using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// The buttons of the "Standard Menu" quick-action toolbar of the original system (docx 1.1): the administrator ticks which of
/// them a user sees. The values are stored in sec.user_quick_actions; the frontend maps each code to its screen.
/// </summary>
public static class QuickActionCodes
{
    public const string Dashboard = "DASHBOARD";
    public const string NewSale = "NEW_SALE";
    public const string SaleReturn = "SALE_RETURN";
    public const string NewPurchase = "NEW_PURCHASE";
    public const string ViewInvoices = "VIEW_INVOICES";
    public const string Recovery = "RECOVERY";
    public const string Payment = "PAYMENT";
    public const string Expense = "EXPENSE";
    public const string AccountLedger = "ACCOUNT_LEDGER";
    public const string Backup = "BACKUP";

    /// <summary>All codes, in the order the toolbar shows them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Dashboard, NewSale, SaleReturn, NewPurchase, ViewInvoices, Recovery, Payment, Expense, AccountLedger, Backup,
    ];

    public static void EnsureAllKnown(IEnumerable<string> actionCodes)
    {
        var unknownCodes = actionCodes.Where(code => !All.Contains(code)).Distinct().ToList();
        if (unknownCodes.Count > 0)
            throw new DomainException(ErrorKind.Validation, $"Unknown quick actions: {string.Join(", ", unknownCodes)}.");
    }
}
