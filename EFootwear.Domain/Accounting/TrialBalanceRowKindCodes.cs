namespace EFootwear.Domain.Accounting;

/// <summary>The kinds of row in a trial balance: an account head (group) with its totals, or one account under its head (detailed report only).</summary>
public static class TrialBalanceRowKindCodes
{
    public const string Group = "GROUP";
    public const string Account = "ACCOUNT";
}
