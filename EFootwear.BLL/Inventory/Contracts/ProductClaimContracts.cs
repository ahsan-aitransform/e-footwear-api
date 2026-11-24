namespace EFootwear.BLL.Inventory.Contracts;

/// <summary>Product Claim (docx 4.16): a request to take pieces out of stock as claimed (damaged, defective, lost). Nothing moves until it is approved.</summary>
public sealed record CreateProductClaimRequest(
    Guid StoreId,
    DateOnly ClaimDate,
    Guid ClientTransactionId,
    Guid VariantId,
    decimal Quantity,
    string ReasonCode,
    string? Note);

/// <summary>Claim Approval (docx 4.17) approves the chosen claims together: either all of them are approved or none is.</summary>
public sealed record ApproveProductClaimsRequest(IReadOnlyList<Guid> ClaimIds);

public sealed record RejectProductClaimRequest(string? Reason);

public sealed record ProductClaimResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    long ClaimNumber,
    DateOnly ClaimDate,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    string? BrandName,
    string? GenderName,
    string? SeasonName,
    decimal Quantity,
    long PurchasePriceAmount,
    string ReasonCode,
    string? Note,
    string StatusCode,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? RejectReason);

public sealed record ProductClaimSearchFilter(Guid? StoreId, DateOnly? FromDate, DateOnly? ToDate, string? StatusCode);

public sealed record ApproveProductClaimsResponse(int ApprovedCount);
