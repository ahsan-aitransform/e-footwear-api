using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// Validation of sizes and barcodes (docx 4.1 Sizes section). A barcode is printable ASCII without spaces, so every code can be
/// printed as Code 128 and typed by a person. When the barcode is left empty the system builds it from the product code and the size
/// (the docx example: product 5127, size 27 gives 5127S27).
/// </summary>
public static class VariantRules
{
    public const int SizeCodeMaximumLength = 20;
    public const int BarcodeMaximumLength = 48;
    public const int MaximumVariantsPerProduct = 200;

    public static string NormalizeSizeCode(string? sizeCode) => (sizeCode ?? "").Trim();

    public static void EnsureValidSizeCode(string sizeCode)
    {
        if (sizeCode.Length == 0)
            throw new DomainException(ErrorKind.Validation, "Every size needs a size name, for example 27.");
        if (sizeCode.Length > SizeCodeMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"A size name can have at most {SizeCodeMaximumLength} characters.");
        if (sizeCode.Any(char.IsControl))
            throw new DomainException(ErrorKind.Validation, "A size name cannot contain control characters.");
    }

    public static string NormalizeBarcode(string? barcode) => (barcode ?? "").Trim();

    public static void EnsureValidBarcode(string barcode)
    {
        if (barcode.Length == 0)
            throw new DomainException(ErrorKind.Validation, "The barcode is empty.");
        if (barcode.Length > BarcodeMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"A barcode can have at most {BarcodeMaximumLength} characters ({barcode}).");
        if (barcode.Any(character => character is < '!' or > '~'))
            throw new DomainException(ErrorKind.Validation,
                $"The barcode {barcode} can only contain letters, digits and symbols, without spaces.");
    }

    /// <summary>The barcode the system proposes: product code, the letter S and the size, without spaces (5127 and 27 give 5127S27).</summary>
    public static string BuildDefaultBarcode(string productCode, string sizeCode) =>
        productCode + "S" + new string(sizeCode.Where(character => !char.IsWhiteSpace(character)).ToArray());
}
