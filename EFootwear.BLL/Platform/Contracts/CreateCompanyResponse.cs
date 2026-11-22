namespace EFootwear.BLL.Platform.Contracts;

public sealed record CreateCompanyResponse(Guid CompanyId, Guid FirstStoreId, Guid OwnerUserId);
