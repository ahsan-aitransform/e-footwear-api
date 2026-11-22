namespace EFootwear.BLL.Platform.Contracts;

public sealed record CompanySummaryResponse(Guid Id, string Name, string Status, string Timezone, string CurrencyCode,
                                            DateTimeOffset CreatedAt);
