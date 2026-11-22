using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core.Settings;

public static class PaymentMethodsSettingsRules
{
    /// <summary>
    /// Every payment method code appears exactly once. A missing method is an error (it is not added silently),
    /// so a client can never drop a method that sales still accepts.
    /// </summary>
    public static PaymentMethodsSettings Normalize(PaymentMethodsSettings settings)
    {
        var methods = settings.Methods ?? [];
        var duplicates = methods.GroupBy(method => method.Code?.Trim()).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0)
            throw new DomainException(ErrorKind.Validation, $"Payment method listed twice: {string.Join(", ", duplicates)}.");

        var normalized = methods.Select(method =>
        {
            var code = method.Code?.Trim() ?? "";
            if (!PaymentMethodCodes.All.Contains(code))
                throw new DomainException(ErrorKind.Validation,
                    $"Unknown payment method {code}. Allowed: {string.Join(", ", PaymentMethodCodes.All)}.");
            if (method.SortOrder < 0 || method.SortOrder > SettingsLimits.PaymentMethodSortOrderMaximum)
                throw new DomainException(ErrorKind.Validation,
                    $"Sort order must be between 0 and {SettingsLimits.PaymentMethodSortOrderMaximum}.");
            return new PaymentMethodSetting(code, method.IsEnabled,
                SettingsValueNormalizer.NormalizeOptionalText(method.DisplayNameEnglish, SettingsLimits.PaymentMethodDisplayNameMaximumLength, "Payment method name"),
                SettingsValueNormalizer.NormalizeOptionalText(method.DisplayNameUrdu, SettingsLimits.PaymentMethodDisplayNameMaximumLength, "Payment method name (Urdu)"),
                method.SortOrder);
        }).ToList();

        var missing = PaymentMethodCodes.All.Where(code => normalized.All(method => method.Code != code)).ToList();
        if (missing.Count > 0)
            throw new DomainException(ErrorKind.Validation, $"Payment methods missing from the list: {string.Join(", ", missing)}.");
        return new PaymentMethodsSettings(normalized.OrderBy(method => method.SortOrder).ThenBy(method => PaymentMethodCodes.All.ToList().IndexOf(method.Code)).ToList());
    }

    /// <summary>The list shown when a company has configured nothing: every method the sales tables accept, switched on.</summary>
    public static PaymentMethodsSettings DefaultWhenNotConfigured() => new(
        PaymentMethodCodes.All.Select((code, index) => new PaymentMethodSetting(code, true, null, null, index)).ToList());
}
