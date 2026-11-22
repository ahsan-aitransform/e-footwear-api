namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// The "Business Info" tab of the docx Preference (Shop Info) screen. Every field is optional; null means "not set".
/// At company level BusinessName and InvoiceText live in platform.companies (name, invoice_terms) and the rest in the
/// BUSINESS_INFO section. A store may override every field except BusinessName (the legal business name belongs to the company).
/// </summary>
public sealed record BusinessInfoSettings(
    string? BusinessName,
    string? BarcodeName,
    string? BusinessNameUrdu,
    string? PhoneNumber,
    string? MobileNumber,
    string? Email,
    string? AddressEnglish,
    string? AddressUrdu,
    string? InvoiceText);
