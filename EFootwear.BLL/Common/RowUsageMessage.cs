using EFootwear.Domain.Common;

namespace EFootwear.BLL.Common;

/// <summary>Builds the "cannot delete, it is used here" error shared by every delete use case.</summary>
public static class RowUsageMessage
{
    /// <summary>Throws a 409 RECORD_IN_USE error when the record is used anywhere. Does nothing when it is free.</summary>
    public static void ThrowWhenUsed(string recordDescription, IReadOnlyList<RowUsageResponse> usages)
    {
        if (usages.Count == 0) return;
        var places = string.Join(", ", usages.Select(usage => $"{usage.TableName} ({usage.UsageCount})"));
        throw new DomainException(ErrorKind.Conflict,
            $"{recordDescription} cannot be deleted because it is still used in: {places}.", ErrorCodes.RecordInUse);
    }
}
