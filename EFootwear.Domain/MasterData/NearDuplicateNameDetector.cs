namespace EFootwear.Domain.MasterData;

/// <summary>
/// Finds names that look like an existing name with a spelling slip ("Sheikhpura" next to "Shiekhupura"). The city list of the
/// old system contains such pairs (docx 2.17), so saving a city warns about them. A warning never blocks saving: a different
/// place can have a similar name.
/// Two names are near-duplicates when, ignoring case, spaces and punctuation, they are not equal but differ by at most
/// <see cref="MaximumEditDistance"/> edits (insert, delete or change one letter, or swap two neighbouring letters) and are long
/// enough to make a coincidence unlikely.
/// </summary>
public static class NearDuplicateNameDetector
{
    public const int MaximumEditDistance = 2;
    public const int MinimumLengthToCompare = 5;

    public static IReadOnlyList<string> FindNearDuplicates(string candidateName, IEnumerable<string> existingNames)
    {
        var candidate = Simplify(candidateName);
        if (candidate.Length < MinimumLengthToCompare) return [];

        var near = new List<string>();
        foreach (var existingName in existingNames)
        {
            var existing = Simplify(existingName);
            if (existing.Length < MinimumLengthToCompare || existing == candidate) continue;
            if (Math.Abs(existing.Length - candidate.Length) > MaximumEditDistance) continue;
            if (EditDistance(candidate, existing) <= MaximumEditDistance)
                near.Add(existingName);
        }
        return near;
    }

    private static string Simplify(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// The optimal-string-alignment distance: the smallest number of single-letter inserts, deletes, changes and swaps of two
    /// neighbouring letters that turn one text into the other. Names are short, so the full table is fine.
    /// </summary>
    private static int EditDistance(string left, string right)
    {
        var table = new int[left.Length + 1, right.Length + 1];
        for (var leftIndex = 0; leftIndex <= left.Length; leftIndex++) table[leftIndex, 0] = leftIndex;
        for (var rightIndex = 0; rightIndex <= right.Length; rightIndex++) table[0, rightIndex] = rightIndex;

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                table[leftIndex, rightIndex] = Math.Min(
                    Math.Min(table[leftIndex - 1, rightIndex] + 1, table[leftIndex, rightIndex - 1] + 1),
                    table[leftIndex - 1, rightIndex - 1] + substitutionCost);

                var isSwapOfNeighbours = leftIndex > 1 && rightIndex > 1
                                         && left[leftIndex - 1] == right[rightIndex - 2]
                                         && left[leftIndex - 2] == right[rightIndex - 1];
                if (isSwapOfNeighbours)
                    table[leftIndex, rightIndex] = Math.Min(table[leftIndex, rightIndex], table[leftIndex - 2, rightIndex - 2] + 1);
            }
        }
        return table[left.Length, right.Length];
    }
}
