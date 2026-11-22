using EFootwear.Domain.Common;

namespace EFootwear.Domain.Accounting;

/// <summary>Validation limits and the rules of a voucher that do not need the database. The database enforces the rest (balance, period, store).</summary>
public static class VoucherRules
{
    public const int DescriptionMaximumLength = 500;
    public const int ReferenceNumberMaximumLength = 100;
    public const int VoidReasonMaximumLength = 500;

    /// <summary>Largest single voucher amount in whole rupees; keeps sums of many vouchers far from the bigint limit.</summary>
    public const long AmountMaximum = 1_000_000_000_000;

    /// <summary>The most rows the printable Voucher Report returns (docx 6.6); a larger period must be narrowed.</summary>
    public const int ReportRowLimit = 5000;

    public const int AllocationCountMaximum = 200;

    public static void EnsureAmountIsValid(long amount)
    {
        if (amount <= 0)
            throw new DomainException(ErrorKind.Validation, "The amount must be more than zero.");
        if (amount > AmountMaximum)
            throw new DomainException(ErrorKind.Validation, "The amount is too large.");
    }

    public static string? CleanText(string? text, int maximumLength, string fieldTitle)
    {
        var cleaned = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (cleaned is not null && cleaned.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{fieldTitle} must be {maximumLength} characters or fewer.");
        return cleaned;
    }
}
