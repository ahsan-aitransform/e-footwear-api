using EFootwear.Domain.Common;

namespace EFootwear.Domain.Accounting;

/// <summary>Limits and filter codes shared by the account reports (docx 8.1 to 8.4, 8.28) and the dropdowns that feed them.</summary>
public static class AccountReportRules
{
    /// <summary>How many choices a searchable account or party dropdown returns at most.</summary>
    public const int LookupResultLimit = 50;

    /// <summary>"From the beginning": the trial balance and account balance add every journal line up to the chosen date.</summary>
    public static readonly DateOnly EarliestReportDate = new(1900, 1, 1);

    public const string MainTypeFilterPrefix = "MAIN_TYPE:";
    public const string GroupFilterPrefix = "GROUP:";
    public const string PartiesFilterPrefix = "PARTIES:";

    public const string PartyTypeCustomer = "CUSTOMER";
    public const string PartyTypeVendor = "VENDOR";
    public const string PartyTypeStaff = "STAFF";

    public const int TypeCodeMaximumLength = 40;

    public static bool IsValidPartyType(string? partyTypeCode) =>
        partyTypeCode is PartyTypeCustomer or PartyTypeVendor or PartyTypeStaff;

    /// <summary>Checks the Account Type filter of the Account Balance report: MAIN_TYPE:&lt;code&gt;, GROUP:&lt;3 digits&gt; or PARTIES:&lt;party type&gt;.</summary>
    public static void EnsureAccountTypeFilterIsValid(string? filterCode)
    {
        if (string.IsNullOrWhiteSpace(filterCode))
            throw new DomainException(ErrorKind.Validation, "Choose an account type.");
        if (filterCode.StartsWith(MainTypeFilterPrefix, StringComparison.Ordinal))
        {
            if (AccountMainTypes.FindByCode(filterCode[MainTypeFilterPrefix.Length..]) is null)
                throw new DomainException(ErrorKind.Validation, "That account type does not exist.");
            return;
        }
        if (filterCode.StartsWith(GroupFilterPrefix, StringComparison.Ordinal))
        {
            var groupCode = filterCode[GroupFilterPrefix.Length..];
            if (groupCode.Length != 3 || !groupCode.All(char.IsAsciiDigit))
                throw new DomainException(ErrorKind.Validation, "That account group does not exist.");
            return;
        }
        if (filterCode.StartsWith(PartiesFilterPrefix, StringComparison.Ordinal) && IsValidPartyType(filterCode[PartiesFilterPrefix.Length..]))
            return;
        throw new DomainException(ErrorKind.Validation, "That account type is not valid.");
    }
}
