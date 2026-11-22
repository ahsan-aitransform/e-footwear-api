namespace EFootwear.Domain.CashManagement;

/// <summary>
/// The note and coin values the cash calculator counts (docx 3.6: 5000, 1000, 500, 100, 50, 20, 10, 5, 2, 1). The same list is a
/// CHECK constraint of cash.count_denominations, so the application and the database accept exactly the same values.
/// </summary>
public static class CashDenominations
{
    public static IReadOnlyList<int> All { get; } = [5000, 1000, 500, 100, 50, 20, 10, 5, 2, 1];

    public static bool IsKnown(int denomination) => All.Contains(denomination);

    /// <summary>Whole rupees: the sum of value x count over the counted denominations.</summary>
    public static long TotalValue(IEnumerable<(int Denomination, int NoteCount)> counts) =>
        counts.Sum(count => (long)count.Denomination * count.NoteCount);
}
