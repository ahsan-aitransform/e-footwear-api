using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;

namespace EFootwear.Tests.Security;

public class PermissionGrantRulesTests
{
    private const string KnownCode = PermissionCodes.UserAccount.ViewUsers.View;
    private const string AnotherKnownCode = PermissionCodes.UserAccount.ViewUsers.Edit;

    [Fact]
    public void Known_codes_are_accepted() =>
        PermissionGrantRules.EnsureCodesAreKnown([KnownCode, AnotherKnownCode]);

    [Fact]
    public void An_unknown_code_is_refused_by_name()
    {
        var exception = Assert.Throws<DomainException>(() => PermissionGrantRules.EnsureCodesAreKnown([KnownCode, "NOT.A.CODE"]));
        Assert.Equal(ErrorKind.Validation, exception.Kind);
        Assert.Contains("NOT.A.CODE", exception.Message);
    }

    [Fact]
    public void A_person_can_add_only_permissions_they_hold()
    {
        IReadOnlySet<string> held = new HashSet<string> { KnownCode };
        PermissionGrantRules.EnsureCanGrant(held, [KnownCode]);
        var exception = Assert.Throws<DomainException>(() => PermissionGrantRules.EnsureCanGrant(held, [KnownCode, AnotherKnownCode]));
        Assert.Equal(ErrorKind.Forbidden, exception.Kind);
        Assert.Equal(ErrorCodes.PermissionDenied, exception.ErrorCode);
    }

    [Fact]
    public void Removing_a_permission_needs_no_such_condition()
    {
        IReadOnlySet<string> current = new HashSet<string> { KnownCode, AnotherKnownCode };
        var change = PermissionGrantRules.Compare(current, [KnownCode]);
        Assert.Empty(change.AddedCodes);
        Assert.Equal([AnotherKnownCode], change.RemovedCodes);
    }

    [Fact]
    public void Compare_reports_only_the_difference_and_an_identical_set_is_empty()
    {
        IReadOnlySet<string> current = new HashSet<string> { KnownCode };
        Assert.True(PermissionGrantRules.Compare(current, [KnownCode]).IsEmpty);
        var change = PermissionGrantRules.Compare(current, [AnotherKnownCode]);
        Assert.Equal([AnotherKnownCode], change.AddedCodes);
        Assert.Equal([KnownCode], change.RemovedCodes);
    }

    [Fact]
    public void Quick_action_codes_must_be_known()
    {
        QuickActionCodes.EnsureAllKnown([QuickActionCodes.NewSale, QuickActionCodes.Backup]);
        Assert.Throws<DomainException>(() => QuickActionCodes.EnsureAllKnown(["MAKE_COFFEE"]));
    }
}
