using EFootwear.Domain.Common;

namespace EFootwear.Domain.CashManagement;

/// <summary>Validation rules of a customer promise (docx 3.5).</summary>
public static class PromiseRules
{
    public const int NoteMaximumLength = 1000;

    /// <summary>The numbering series (core.number_series) the promise numbers of a store are drawn from.</summary>
    public const string NumberSeriesCode = "PROMISE";

    /// <summary>Trims the description and checks its length; an empty description becomes null.</summary>
    public static string? NormalizeNote(string? note)
    {
        var normalized = string.Join(' ', (note ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return null;
        if (normalized.Length > NoteMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The description can have at most {NoteMaximumLength} characters.");
        return normalized;
    }

    public static void EnsureAmountAndDates(long promisedAmount, DateOnly promiseDate, DateOnly dueDate)
    {
        if (promisedAmount <= 0)
            throw new DomainException(ErrorKind.Validation, "The promised amount must be above zero.");
        if (dueDate < promiseDate)
            throw new DomainException(ErrorKind.Validation, "The promise date cannot be before the day the promise was taken.");
    }
}
