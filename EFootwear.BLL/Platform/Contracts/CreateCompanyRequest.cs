namespace EFootwear.BLL.Platform.Contracts;

public sealed record CreateCompanyRequest(string Name, string? Timezone, string OwnerEmail, string OwnerFullName,
                                          string OwnerPassword, string FirstStoreCode, string FirstStoreName);
