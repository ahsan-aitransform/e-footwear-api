namespace EFootwear.BLL.Staff.Contracts;

/// <summary>A light staff row for a dropdown on another screen (a salesman on a sale, a payee on an expense). Any signed-in company user may read it.</summary>
public sealed record StaffMemberLookupItemResponse(Guid Id, string Code, string Name, string? Phone, Guid StoreId);
