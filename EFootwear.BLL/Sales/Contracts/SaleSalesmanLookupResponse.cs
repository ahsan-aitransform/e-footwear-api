namespace EFootwear.BLL.Sales.Contracts;

public sealed record SaleSalesmanLookupResponse(Guid StaffId, string Code, string Name, bool IsManager);
