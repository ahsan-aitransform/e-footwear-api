namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One choice of the Account Type dropdown. <c>FilterCode</c> is what the report endpoint accepts: <c>MAIN_TYPE:ASSET</c>,
/// <c>GROUP:103</c>, or <c>PARTIES:CUSTOMER</c> / <c>PARTIES:VENDOR</c> / <c>PARTIES:STAFF</c> (the docx lists Customers and Vendors beside the account types).
/// </summary>
public sealed record AccountBalanceTypeOptionResponse(string FilterCode, string Title);
