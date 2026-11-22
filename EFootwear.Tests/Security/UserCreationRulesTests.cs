using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Tests.Security;

/// <summary>
/// The user-creation hierarchy, as pure rules (no database).
///   platform admin -> company owner, store manager
///   company owner  -> store manager, cashier, accountant (any store of the company)
///   store manager  -> cashier, accountant (own stores only)
/// </summary>
public class UserCreationRulesTests
{
    private static Caller PlatformAdministrator() => new(true, null, true, new HashSet<Guid>());
    private static Caller CompanyOwner() => new(false, RoleCodes.CompanyOwner, true, new HashSet<Guid>());
    private static Caller StoreManager(params Guid[] storeIds) => new(false, RoleCodes.StoreManager, false, storeIds.ToHashSet());
    private static Caller Cashier(params Guid[] storeIds) => new(false, RoleCodes.CashierSalesman, false, storeIds.ToHashSet());

    private static Guid StoreIdNumber(int number) => new(number, 0, 0, new byte[8]);

    private static void AssertErrorKind(ErrorKind expectedKind, Action action)
    {
        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public void Platform_admin_creates_company_owner_without_stores()
    {
        UserCreationRules.EnsureCanCreate(PlatformAdministrator(), RoleCodes.CompanyOwner, []);
    }

    [Fact]
    public void Platform_admin_creates_store_manager_for_exactly_one_store()
    {
        UserCreationRules.EnsureCanCreate(PlatformAdministrator(), RoleCodes.StoreManager, [StoreIdNumber(2)]);
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(
            PlatformAdministrator(), RoleCodes.StoreManager, [StoreIdNumber(2), StoreIdNumber(3)]));
    }

    [Fact]
    public void Platform_admin_cannot_create_cashiers()
    {
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(
            PlatformAdministrator(), RoleCodes.CashierSalesman, [StoreIdNumber(1)]));
    }

    [Fact]
    public void Owner_creates_managers_and_staff_in_any_store()
    {
        UserCreationRules.EnsureCanCreate(CompanyOwner(), RoleCodes.StoreManager, [StoreIdNumber(5)]);
        UserCreationRules.EnsureCanCreate(CompanyOwner(), RoleCodes.CashierSalesman, [StoreIdNumber(1), StoreIdNumber(2)]);
        UserCreationRules.EnsureCanCreate(CompanyOwner(), RoleCodes.Accountant, [StoreIdNumber(7)]);
    }

    [Fact]
    public void Owner_cannot_create_another_owner()
    {
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(CompanyOwner(), RoleCodes.CompanyOwner, []));
    }

    [Fact]
    public void A_company_owner_is_created_without_stores()
    {
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(
            PlatformAdministrator(), RoleCodes.CompanyOwner, [StoreIdNumber(1)]));
    }

    [Fact]
    public void Store_manager_creates_staff_only_in_own_stores()
    {
        UserCreationRules.EnsureCanCreate(StoreManager(StoreIdNumber(4)), RoleCodes.CashierSalesman, [StoreIdNumber(4)]);
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(
            StoreManager(StoreIdNumber(4)), RoleCodes.CashierSalesman, [StoreIdNumber(9)]));
    }

    [Fact]
    public void Store_manager_cannot_create_managers_or_owners()
    {
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(
            StoreManager(StoreIdNumber(4)), RoleCodes.StoreManager, [StoreIdNumber(4)]));
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(
            StoreManager(StoreIdNumber(4)), RoleCodes.CompanyOwner, []));
    }

    [Fact]
    public void Cashier_creates_nobody()
    {
        AssertErrorKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(
            Cashier(StoreIdNumber(4)), RoleCodes.CashierSalesman, [StoreIdNumber(4)]));
    }

    [Fact]
    public void Store_staff_need_a_store_and_never_head_office()
    {
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(CompanyOwner(), RoleCodes.CashierSalesman, []));
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(
            CompanyOwner(), RoleCodes.CashierSalesman, [StoreIdNumber(0)]));
    }

    [Fact]
    public void Creatable_roles_match_the_hierarchy()
    {
        Assert.Equal([RoleCodes.CompanyOwner, RoleCodes.StoreManager], UserCreationRules.CreatableRoles(PlatformAdministrator()));
        Assert.Equal([RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant], UserCreationRules.CreatableRoles(CompanyOwner()));
        Assert.Equal([RoleCodes.CashierSalesman, RoleCodes.Accountant], UserCreationRules.CreatableRoles(StoreManager(StoreIdNumber(1))));
        Assert.Empty(UserCreationRules.CreatableRoles(Cashier(StoreIdNumber(1))));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("no at@dot")]
    [InlineData("a@b")]
    public void Email_pattern_is_enforced(string email)
    {
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount(email, "Full Name", "Password1"));
    }

    [Fact]
    public void Password_needs_minimum_length_and_full_name_is_required()
    {
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "Full Name", "short"));
        AssertErrorKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "  ", "Password1"));
        UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "Full Name", "Password1");
    }
}
