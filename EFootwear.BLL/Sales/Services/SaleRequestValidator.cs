using EFootwear.BLL.Sales.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core.Settings;
using EFootwear.Domain.Sales;

namespace EFootwear.BLL.Sales.Services;

/// <summary>
/// The checks a sale, a return or an exchange request passes before the database function is called, so a person gets one clear answer instead of a
/// database message. The functions in migration 0500 repeat every rule that protects the books (they stay the authority); the rules here only
/// shape the request. Nothing in this class is a business policy.
/// </summary>
public static class SaleRequestValidator
{
    public static void EnsureSaleLinesAreWellFormed(IReadOnlyList<SaleLineRequest>? lines, string listTitle = "sale")
    {
        if (lines is null || lines.Count == 0)
            throw new DomainException(ErrorKind.Validation, $"Add at least one item to the {listTitle}.");
        if (lines.Count > SaleRules.MaximumLinesPerInvoice)
            throw new DomainException(ErrorKind.Validation, $"A {listTitle} can have at most {SaleRules.MaximumLinesPerInvoice} lines.");

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (line.VariantId == Guid.Empty)
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: choose an item.");
            SaleRules.EnsureQuantityIsValid(line.Quantity, lineNumber);
            if (line.UnitPrice < 0)
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: the price cannot be negative.");
            var discountType = line.DiscountTypeCode ?? SaleLineDiscountTypeCodes.None;
            if (!SaleLineDiscountTypeCodes.IsValid(discountType))
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: the discount type is not valid.");
            if (discountType != SaleLineDiscountTypeCodes.None && (line.DiscountValue is null or <= 0))
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: enter the discount, or remove it.");
        }
    }

    public static void EnsureReturnedLinesAreWellFormed(IReadOnlyList<ReturnedLineRequest>? lines)
    {
        if (lines is null || lines.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Add at least one returned item.");
        if (lines.Count > SaleRules.MaximumLinesPerInvoice)
            throw new DomainException(ErrorKind.Validation, $"A return can have at most {SaleRules.MaximumLinesPerInvoice} lines.");

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (line.OriginalLineId == Guid.Empty)
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: choose the sale line that is being returned.");
            SaleRules.EnsureQuantityIsValid(line.Quantity, lineNumber);
            if (line.ItemConditionCode is not null && !ReturnedItemConditionCodes.IsValid(line.ItemConditionCode))
                throw new DomainException(ErrorKind.Validation, $"Line {lineNumber}: the item condition must be sellable or damaged.");
            SaleRules.CleanOptionalText(line.ReasonText, SaleRules.ReasonMaximumLength, $"Line {lineNumber}: the return reason");
        }
    }

    /// <summary>
    /// Payments: at most six rows, an amount above zero each, a method the company has switched on, a cash return only with a cash payment.
    /// The books check the rest (the account must be a cash or bank account; the payment may not exceed the bill).
    /// </summary>
    public static void EnsurePaymentsAreWellFormed(
        IReadOnlyList<SalePaymentRequest>? payments, long cashReturnedAmount, IReadOnlyCollection<string> enabledPaymentMethodCodes)
    {
        var rows = payments ?? [];
        if (rows.Count > SalePaymentRules.MaximumPaymentsPerInvoice)
            throw new DomainException(ErrorKind.Validation, $"A sale can be paid with at most {SalePaymentRules.MaximumPaymentsPerInvoice} payments.");
        if (cashReturnedAmount < 0)
            throw new DomainException(ErrorKind.Validation, "The cash returned cannot be negative.");

        foreach (var payment in rows)
        {
            if (!PaymentMethodCodes.All.Contains(payment.PaymentMethodCode))
                throw new DomainException(ErrorKind.Validation, "The payment method is not valid.");
            if (!enabledPaymentMethodCodes.Contains(payment.PaymentMethodCode))
                throw new DomainException(ErrorKind.Validation, $"The payment method {payment.PaymentMethodCode} is switched off for this company.");
            if (payment.Amount <= 0)
                throw new DomainException(ErrorKind.Validation, "Every payment needs an amount above zero.");
            if (payment.PaymentAccountId == Guid.Empty)
                throw new DomainException(ErrorKind.Validation, "Choose the account the payment goes to.");
            SaleRules.CleanOptionalText(payment.ReferenceNumber, SalePaymentRules.ReferenceNumberMaximumLength, "The payment reference");
        }
        if (cashReturnedAmount > 0 && rows.All(payment => payment.PaymentMethodCode != PaymentMethodCodes.Cash))
            throw new DomainException(ErrorKind.Validation, "Cash can only be returned when part of the payment is cash.");
    }

    public static void EnsureSaleHeaderIsWellFormed(Guid clientTransactionId, DateOnly date, string? description, string? walkInName, string? walkInMobile)
    {
        if (clientTransactionId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "The request needs a client transaction id (a new Guid for each sale, sent again unchanged when a save is retried).");
        if (date == default)
            throw new DomainException(ErrorKind.Validation, "Choose the date.");
        SaleRules.CleanOptionalText(description, SaleRules.DescriptionMaximumLength, "The description");
        SaleRules.CleanOptionalText(walkInName, SaleRules.WalkInNameMaximumLength, "The customer name");
        SaleRules.CleanOptionalText(walkInMobile, SaleRules.WalkInMobileMaximumLength, "The mobile number");
    }
}
