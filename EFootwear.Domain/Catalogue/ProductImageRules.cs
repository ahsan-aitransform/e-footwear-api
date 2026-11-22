using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>Limits of the Images tab (docx 4.1.4): up to four pictures per product. The type is read from the file's first bytes, never from what the client says.</summary>
public static class ProductImageRules
{
    public const int SlotCount = 4;
    public const int MaximumSizeBytes = 2 * 1024 * 1024;

    public static void EnsureValidSlot(int slot)
    {
        if (slot is < 1 or > SlotCount)
            throw new DomainException(ErrorKind.Validation, $"The picture slot must be between 1 and {SlotCount}.");
    }
}
