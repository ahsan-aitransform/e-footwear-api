namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// The paper and language the screen offers first. They come from the Printing settings of the store (else the company); when nothing is configured the
/// docx defaults apply (thermal paper, English, one copy). The cashier can still choose another paper and language for a print.
/// </summary>
public sealed record SalePrintDefaultsResponse(string PrintFormatCode, string PrintLanguageCode, bool PrintRightToLeft, bool PrintLogoOnDocuments, int ReceiptCopies);
