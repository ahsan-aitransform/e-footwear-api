namespace EFootwear.BLL.Sales.Contracts;

public sealed record SaleSalesmanLookupResponse(Guid PartyId, string Code, string Name, bool IsManager);
