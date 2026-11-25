namespace EFootwear.BLL.Customers.Contracts;

/// <summary>A light customer row for a dropdown on another screen. Any signed-in company user may read it.</summary>
public sealed record CustomerLookupItemResponse(Guid Id, string Code, string Name, string? Phone);
