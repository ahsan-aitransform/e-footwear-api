using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;

namespace EFootwear.Domain.Catalogue;

/// <summary>Validation of a price band (a named range of list prices). Overlapping ranges are refused by the database as well.</summary>
public static class PriceBandRules
{
    public const int NameMaximumLength = 80;
    public const int SortOrderMaximum = 100_000;

    public static string NormalizeName(string? name) => MasterListRules.NormalizeText(name);

    public static void EnsureValid(string name, long minimumPrice, long? maximumPrice, int sortOrder)
    {
        if (name.Length == 0)
            throw new DomainException(ErrorKind.Validation, "The price band needs a name.");
        if (name.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The price band name can have at most {NameMaximumLength} characters.");
        CataloguePriceRules.EnsureValidPrice(minimumPrice, "minimum price");
        if (maximumPrice is { } maximum)
        {
            CataloguePriceRules.EnsureValidPrice(maximum, "maximum price");
            if (maximum < minimumPrice)
                throw new DomainException(ErrorKind.Validation, "The maximum price cannot be lower than the minimum price.");
        }
        if (sortOrder is < 0 or > SortOrderMaximum)
            throw new DomainException(ErrorKind.Validation, $"The sort order must be between 0 and {SortOrderMaximum}.");
    }
}
