namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>An account in a searchable dropdown (Account Ledger: Store / Type / Account, or directly by Account #).</summary>
public sealed record AccountLookupItemResponse(
    Guid AccountId,
    string Code,
    string Title,
    string MainTypeCode,
    string GroupCode,
    string GroupTitle,
    Guid StoreId,
    string StoreName);
