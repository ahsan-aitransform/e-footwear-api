namespace EFootwear.Domain.Accounting;

/// <summary>
/// One of the six main types of the chart of accounts (docx 2.3: 1 Assets, 2 Liabilities, 3 Capital, 4 Income, 5 Stock, 6 Expenses).
/// <see cref="Code"/> is the value stored in acct.accounts.main_type; the first digit of every group and account code is
/// <see cref="DigitPrefix"/>.
/// </summary>
public sealed record AccountMainType(string Code, char DigitPrefix, string DisplayName);
