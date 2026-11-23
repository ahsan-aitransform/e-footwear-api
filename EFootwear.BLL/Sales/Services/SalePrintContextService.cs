using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.Domain.Core.Settings;

namespace EFootwear.BLL.Sales.Services;

/// <summary>
/// The business header and the paper defaults of a printed document (Part 11 printing, Part 7 receipt printing). They are read from the settings the
/// Settings module stores, with the own overrides of the store. Nothing is invented: a value that was never configured falls back to the docx defaults
/// for the screen (thermal paper, English, left to right, one copy), which the cashier can change for each print.
/// </summary>
public sealed class SalePrintContextService(CompanySettingsService companySettingsService, CompanyLogoService companyLogoService)
{
    public const int DefaultReceiptCopies = 1;

    public async Task<SaleBusinessHeaderResponse> BuildBusinessHeaderAsync(Guid storeId, string storeName, CancellationToken cancellationToken)
    {
        var businessInfo = (await companySettingsService.GetStoreBusinessInfoAsync(storeId, cancellationToken)).Effective;
        var logo = await companyLogoService.GetCurrentAsync(cancellationToken);
        return new SaleBusinessHeaderResponse(
            businessInfo.BusinessName ?? storeName, businessInfo.BusinessNameUrdu, storeName, businessInfo.PhoneNumber, businessInfo.MobileNumber,
            businessInfo.Email, businessInfo.AddressEnglish, businessInfo.AddressUrdu, businessInfo.InvoiceText, logo is not null);
    }

    public async Task<SalePrintDefaultsResponse> BuildPrintDefaultsAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var printing = (await companySettingsService.GetStorePrintingAsync(storeId, cancellationToken)).Effective;
        return new SalePrintDefaultsResponse(
            printing.DefaultPrintFormat ?? PrintFormatCodes.Thermal,
            printing.PrintLanguageCode ?? SupportedLanguageCodes.English,
            printing.PrintRightToLeft ?? false,
            printing.PrintLogoOnDocuments ?? true,
            printing.ReceiptCopies ?? DefaultReceiptCopies);
    }
}
