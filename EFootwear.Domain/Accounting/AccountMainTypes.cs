namespace EFootwear.Domain.Accounting;

/// <summary>The main types of the chart of accounts, in the order of the docx. The single source for codes, digits and titles.</summary>
public static class AccountMainTypes
{
    public static readonly AccountMainType Asset = new("ASSET", '1', "Assets");
    public static readonly AccountMainType Liability = new("LIABILITY", '2', "Liabilities");
    public static readonly AccountMainType Capital = new("CAPITAL", '3', "Capital");
    public static readonly AccountMainType Income = new("INCOME", '4', "Income");
    public static readonly AccountMainType Stock = new("STOCK", '5', "Stock");
    public static readonly AccountMainType Expense = new("EXPENSE", '6', "Expenses");

    public static IReadOnlyList<AccountMainType> All { get; } = [Asset, Liability, Capital, Income, Stock, Expense];

    public static AccountMainType? FindByCode(string? code) =>
        All.FirstOrDefault(mainType => string.Equals(mainType.Code, code, StringComparison.OrdinalIgnoreCase));

    public static AccountMainType? FindByDigit(char digit) => All.FirstOrDefault(mainType => mainType.DigitPrefix == digit);
}
