using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Tests;

/// <summary>
/// The user-creation hierarchy, as pure rules (no database).
///   platform admin -> company owner, store manager
///   company owner  -> store manager, cashier, accountant (any store of the company)
///   store manager  -> cashier, accountant (own stores only)
/// </summary>
public class UserCreationRulesTests
{
    private static Caller Platform() => new(true, null, true, new HashSet<long>());
    private static Caller Owner() => new(false, RoleCodes.CompanyOwner, true, new HashSet<long>());
    private static Caller Manager(params long[] stores) => new(false, RoleCodes.StoreManager, false, stores.ToHashSet());
    private static Caller Cashier(params long[] stores) => new(false, RoleCodes.CashierSalesman, false, stores.ToHashSet());

    private static void AssertKind(ErrorKind kind, Action act)
    {
        var ex = Assert.Throws<DomainException>(act);
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public void Platform_admin_creates_company_owner_without_stores()
    {
        UserCreationRules.EnsureCanCreate(Platform(), RoleCodes.CompanyOwner, []);
    }

    [Fact]
    public void Platform_admin_creates_store_manager_for_exactly_one_store()
    {
        UserCreationRules.EnsureCanCreate(Platform(), RoleCodes.StoreManager, [2]);
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(Platform(), RoleCodes.StoreManager, [2, 3]));
    }

    [Fact]
    public void Platform_admin_cannot_create_cashiers()
    {
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Platform(), RoleCodes.CashierSalesman, [1]));
    }

    [Fact]
    public void Owner_creates_managers_and_staff_in_any_store()
    {
        UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.StoreManager, [5]);
        UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.CashierSalesman, [1, 2]);
        UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.Accountant, [7]);
    }

    [Fact]
    public void Owner_cannot_create_another_owner()
    {
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.CompanyOwner, []));
    }

    [Fact]
    public void A_company_owner_is_created_without_stores()
    {
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(Platform(), RoleCodes.CompanyOwner, [1]));
    }

    [Fact]
    public void Store_manager_creates_staff_only_in_own_stores()
    {
        UserCreationRules.EnsureCanCreate(Manager(4), RoleCodes.CashierSalesman, [4]);
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Manager(4), RoleCodes.CashierSalesman, [9]));
    }

    [Fact]
    public void Store_manager_cannot_create_managers_or_owners()
    {
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Manager(4), RoleCodes.StoreManager, [4]));
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Manager(4), RoleCodes.CompanyOwner, []));
    }

    [Fact]
    public void Cashier_creates_nobody()
    {
        AssertKind(ErrorKind.Forbidden, () => UserCreationRules.EnsureCanCreate(Cashier(4), RoleCodes.CashierSalesman, [4]));
    }

    [Fact]
    public void Store_staff_need_a_store_and_never_head_office()
    {
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.CashierSalesman, []));
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureCanCreate(Owner(), RoleCodes.CashierSalesman, [0]));
    }

    [Fact]
    public void Creatable_roles_match_the_hierarchy()
    {
        Assert.Equal([RoleCodes.CompanyOwner, RoleCodes.StoreManager], UserCreationRules.CreatableRoles(Platform()));
        Assert.Equal([RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant], UserCreationRules.CreatableRoles(Owner()));
        Assert.Equal([RoleCodes.CashierSalesman, RoleCodes.Accountant], UserCreationRules.CreatableRoles(Manager(1)));
        Assert.Empty(UserCreationRules.CreatableRoles(Cashier(1)));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("no at@dot")]
    [InlineData("a@b")]
    public void Email_pattern_is_enforced(string username)
    {
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount(username, "Full Name", "Password1"));
    }

    [Fact]
    public void Password_needs_minimum_length_and_full_name_is_required()
    {
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "Full Name", "short"));
        AssertKind(ErrorKind.Validation, () => UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "  ", "Password1"));
        UserCreationRules.EnsureValidAccount("valid.user@shop.pk", "Full Name", "Password1");
    }
}
