namespace EFootwear.Domain.Platform;

/// <summary>platform.companies.price_band_basis: whether price bands are measured on the list price or the net price.</summary>
public static class PriceBandBasisCodes
{
    public const string ListPrice = "LIST";
    public const string NetPrice = "NET";

    public static readonly IReadOnlyList<string> All = [ListPrice, NetPrice];
}
