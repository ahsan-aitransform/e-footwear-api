namespace EFootwear.Domain.HumanResources;

/// <summary>How often a staff member is paid. The values mirror the CHECK on <c>party.staff_profiles.pay_frequency</c> and <c>hr.salary_terms</c>.</summary>
public static class PayFrequencyCodes
{
    public const string Monthly = "MONTHLY";
    public const string Biweekly = "BIWEEKLY";
    public const string Weekly = "WEEKLY";

    public static readonly IReadOnlyList<string> All = [Monthly, Biweekly, Weekly];

    public static bool IsValid(string? payFrequencyCode) => payFrequencyCode is Monthly or Biweekly or Weekly;
}
