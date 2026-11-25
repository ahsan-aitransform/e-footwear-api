namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>A light vendor row for a dropdown on another screen. Any signed-in company user may read it.</summary>
public sealed record VendorLookupItemResponse(Guid Id, string Code, string Name, string? Phone);
