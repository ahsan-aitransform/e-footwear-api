namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>An active item for a dropdown on another screen (a customer's city, a product's brand). Needs no Settings permission.</summary>
public sealed record MasterListOptionResponse(Guid Id, string? Code, string Name, string? NameUrdu);
