using EFootwear.Domain.Accounting;
using EFootwear.Domain.CashManagement;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Parties;

namespace EFootwear.Tests.MasterData;

/// <summary>Pure rules of the master data, party and cash modules: no database needed.</summary>
public class MasterDataRulesTests
{
    [Theory]
    [InlineData("Sheikhpura", "Shiekhupura")]
    [InlineData("Shiekhupura", "Sheikhpura")]
    [InlineData("Kot Addu", "Kot Adu")]
    [InlineData("LAYYAH", "layah")]
    public void Names_that_look_like_a_spelling_slip_are_reported(string newName, string existingName)
    {
        Assert.Equal([existingName], NearDuplicateNameDetector.FindNearDuplicates(newName, [existingName]));
    }

    [Theory]
    [InlineData("Multan", "Lahore")]
    [InlineData("Karachi", "Kasur")]
    [InlineData("Lahore", "Lahore")]        // an identical name is a duplicate, not a spelling slip
    [InlineData("Sahiwal", "sahiwal")]
    [InlineData("Taunsa", "Taun")]          // the existing name is too short to compare (fewer than five letters)
    public void Different_or_identical_names_are_not_reported(string newName, string existingName)
    {
        Assert.Empty(NearDuplicateNameDetector.FindNearDuplicates(newName, [existingName]));
    }

    [Fact]
    public void Text_is_trimmed_and_inner_spaces_are_collapsed()
    {
        Assert.Equal("Men Shoes", MasterListRules.NormalizeText("  Men    Shoes  "));
        Assert.Null(MasterListRules.NormalizeOptionalText("   "));
    }

    [Fact]
    public void A_master_list_item_needs_a_name_within_the_limits()
    {
        Assert.Throws<DomainException>(() => MasterListRules.EnsureValid("", null, null, 0, "Department"));
        Assert.Throws<DomainException>(() => MasterListRules.EnsureValid(new string('x', 121), null, null, 0, "Department"));
        Assert.Throws<DomainException>(() => MasterListRules.EnsureValid("Shoes", null, null, -1, "Department"));
        MasterListRules.EnsureValid("Shoes", "جوتے", "SH", 5, "Department");
    }

    [Fact]
    public void The_next_group_code_follows_the_highest_serial_of_the_main_type()
    {
        Assert.Equal("108", ChartOfAccountsRules.NextGroupCode(AccountMainTypes.Asset, ["101", "102", "107", "205"]));
        Assert.Equal("201", ChartOfAccountsRules.NextGroupCode(AccountMainTypes.Liability, ["101", "102"]));
        Assert.Equal("601", ChartOfAccountsRules.NextGroupCode(AccountMainTypes.Expense, []));
        Assert.Throws<DomainException>(() => ChartOfAccountsRules.NextGroupCode(AccountMainTypes.Asset, ["199"]));
    }

    [Theory]
    [InlineData("102", true)]
    [InlineData("699", true)]
    [InlineData("002", false)]
    [InlineData("702", false)]
    [InlineData("10", false)]
    [InlineData("1020", false)]
    [InlineData(null, false)]
    public void A_group_code_is_three_digits_starting_with_a_main_type_digit(string? groupCode, bool expected)
    {
        Assert.Equal(expected, ChartOfAccountsRules.IsValidGroupCode(groupCode));
    }

    [Fact]
    public void The_six_main_types_have_the_digits_of_the_docx()
    {
        Assert.Equal("123456", string.Concat(AccountMainTypes.All.Select(mainType => mainType.DigitPrefix)));
        Assert.Equal(AccountMainTypes.Capital, AccountMainTypes.FindByCode("capital"));
        Assert.Null(AccountMainTypes.FindByCode("PROFIT"));
    }

    [Theory]
    [InlineData("35202-1234567-1", true)]
    [InlineData("3520212345671", false)]
    [InlineData("35202-123456-1", false)]
    [InlineData("abcde-1234567-1", false)]
    public void A_cnic_has_the_shape_12345_1234567_1(string cnic, bool expected)
    {
        Assert.Equal(expected, PartyRules.IsValidCnic(cnic));
    }

    [Theory]
    [InlineData("0300 1234567", true)]
    [InlineData("+92-300-1234567", true)]
    [InlineData("12", false)]
    [InlineData("phone", false)]
    public void A_phone_number_is_digits_with_optional_separators(string phone, bool expected)
    {
        Assert.Equal(expected, PartyRules.IsValidPhone(phone));
    }

    [Fact]
    public void A_party_needs_at_least_one_type()
    {
        Assert.Throws<DomainException>(() => PartyRules.EnsureAtLeastOneRole(false, false, false));
        PartyRules.EnsureAtLeastOneRole(false, true, false);
    }

    [Fact]
    public void The_counted_cash_is_the_sum_of_value_times_notes_in_whole_rupees()
    {
        var total = CashDenominations.TotalValue([(5000, 2), (1000, 3), (500, 1), (100, 4), (50, 0), (20, 5), (10, 1), (5, 2), (2, 3), (1, 7)]);
        Assert.Equal(10_000 + 3_000 + 500 + 400 + 0 + 100 + 10 + 10 + 6 + 7, total);
        Assert.Equal(0, CashDenominations.TotalValue([]));
    }

    [Fact]
    public void Only_the_documented_denominations_are_known()
    {
        Assert.Equal([5000, 1000, 500, 100, 50, 20, 10, 5, 2, 1], CashDenominations.All);
        Assert.True(CashDenominations.IsKnown(20));
        Assert.False(CashDenominations.IsKnown(200));
    }

    [Fact]
    public void A_promise_needs_a_positive_amount_and_a_due_date_that_is_not_before_the_promise_date()
    {
        var today = new DateOnly(2026, 11, 20);
        PromiseRules.EnsureAmountAndDates(1, today, today);
        Assert.Throws<DomainException>(() => PromiseRules.EnsureAmountAndDates(0, today, today));
        Assert.Throws<DomainException>(() => PromiseRules.EnsureAmountAndDates(500, today, today.AddDays(-1)));
    }
}
