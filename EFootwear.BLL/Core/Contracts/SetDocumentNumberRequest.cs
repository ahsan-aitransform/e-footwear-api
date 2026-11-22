namespace EFootwear.BLL.Core.Contracts;

public sealed record SetDocumentNumberRequest(Guid StoreId, string SeriesCode, long NextValue);
