using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.Domain.Accounting;

/// <summary>Limits and filter codes shared by the account reports (docx 8.1 to 8.4, 8.28) and the dropdowns that feed them.</summary>
public static class AccountReportRules
{
    /// <summary>How many choices a searchable account or contact dropdown returns at most.</summary>
    public const int LookupResultLimit = 50;

    /// <summary>"From the beginning": the trial balance and account balance add every journal line up to the chosen date.</summary>
    public static readonly DateOnly EarliestReportDate = new(1900, 1, 1);

    public const string MainTypeFilterPrefix = "MAIN_TYPE:";
    public const string GroupFilterPrefix = "GROUP:";
    public const string ContactsFilterPrefix = "CONTACTS:";

    public const int TypeCodeMaximumLength = 40;

    /// <summary>Checks the Account Type filter of the Account Balance report: MAIN_TYPE:&lt;code&gt;, GROUP:&lt;3 digits&gt; or CONTACTS:&lt;CUSTOMER, VENDOR or STAFF&gt;.</summary>
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
        if (filterCode.StartsWith(ContactsFilterPrefix, StringComparison.Ordinal) && ContactKindCodes.IsValid(filterCode[ContactsFilterPrefix.Length..]))
            return;
        throw new DomainException(ErrorKind.Validation, "That account type is not valid.");
    }
}
