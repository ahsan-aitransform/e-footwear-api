namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>An account of the chart (docx 2.1 grid: Code, Group, Title). System accounts cannot be deleted or deactivated.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Code,
    string GroupCode,
    string GroupName,
    string MainTypeCode,
    string Name,
    Guid StoreId,
    string? SystemRole,
    bool IsPostable,
    bool IsSubledgerControl,
    bool IsActive,
    long RowVersion);
