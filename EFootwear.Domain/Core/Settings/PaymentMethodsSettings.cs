namespace EFootwear.Domain.Core.Settings;

/// <summary>The payment methods of a company, in the order the user should see them.</summary>
public sealed record PaymentMethodsSettings(IReadOnlyList<PaymentMethodSetting> Methods);
