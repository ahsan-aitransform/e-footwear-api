namespace EFootwear.Domain.Sales;

/// <summary>
/// The condition of a returned item (Part 12 "sellable versus damaged stock"). It is recorded on the return line. What a DAMAGED item does to the stock
/// is [BUSINESS DECISION REQUIRED]: today every returned item goes back into stock whatever its condition.
/// </summary>
public static class ReturnedItemConditionCodes
{
    public const string Sellable = "SELLABLE";
    public const string Damaged = "DAMAGED";

    public static bool IsValid(string? code) => code is Sellable or Damaged;
}
