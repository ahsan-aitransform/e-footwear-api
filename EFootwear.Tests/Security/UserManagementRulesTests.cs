using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Tests.Security;

/// <summary>Who may change or remove ANOTHER user's account: the pure rules, without a database.</summary>
public class UserManagementRulesTests
{
    private static readonly Guid FirstStoreId = Guid.NewGuid();
    private static readonly Guid SecondStoreId = Guid.NewGuid();

    private static Caller Owner() => new(false, RoleCodes.CompanyOwner, true, new HashSet<Guid>());
    private static Caller ManagerOfFirstStore() => new(false, RoleCodes.StoreManager, false, new HashSet<Guid> { FirstStoreId });

    private static ManagedUserSummary User(string roleCode, params Guid[] storeIds) =>
        new(Guid.NewGuid(), roleCode, storeIds.ToHashSet());

    private static ErrorKind KindOf(Action action) => Assert.Throws<DomainException>(action).Kind;

    [Theory]
    [InlineData(UserManagementAction.EditProfile)]
    [InlineData(UserManagementAction.ChangeStatus)]
    [InlineData(UserManagementAction.ResetPassword)]
    [InlineData(UserManagementAction.Delete)]
    [InlineData(UserManagementAction.ChangePermissions)]
    public void The_owner_may_manage_every_role_below_the_owner(UserManagementAction action) =>
        UserManagementRules.EnsureCanManage(Owner(), Guid.NewGuid(), User(RoleCodes.Accountant, FirstStoreId), action);

    [Theory]
    [InlineData(UserManagementAction.View)]
    [InlineData(UserManagementAction.EditProfile)]
    [InlineData(UserManagementAction.ChangeStatus)]
    [InlineData(UserManagementAction.ResetPassword)]
    [InlineData(UserManagementAction.Delete)]
    [InlineData(UserManagementAction.ChangePermissions)]
    [InlineData(UserManagementAction.ManageSessions)]
    public void The_company_owner_account_is_protected_from_everybody_else(UserManagementAction action) =>
        Assert.Equal(ErrorKind.Forbidden, KindOf(() =>
            UserManagementRules.EnsureCanManage(ManagerOfFirstStore(), Guid.NewGuid(), User(RoleCodes.CompanyOwner), action)));

    [Theory]
    [InlineData(UserManagementAction.ChangeStatus)]
    [InlineData(UserManagementAction.Delete)]
    [InlineData(UserManagementAction.ChangePermissions)]
    [InlineData(UserManagementAction.ChangeRoleOrStores)]
    [InlineData(UserManagementAction.ResetPassword)]
    public void Nobody_may_block_delete_reset_or_raise_their_own_account(UserManagementAction action)
    {
        var ownUserId = Guid.NewGuid();
        var own = new ManagedUserSummary(ownUserId, RoleCodes.StoreManager, new HashSet<Guid> { FirstStoreId });
        Assert.Equal(ErrorKind.Forbidden, KindOf(() => UserManagementRules.EnsureCanManage(ManagerOfFirstStore(), ownUserId, own, action)));
    }

    [Theory]
    [InlineData(UserManagementAction.View)]
    [InlineData(UserManagementAction.EditProfile)]
    [InlineData(UserManagementAction.ManageSessions)]
    public void Everybody_may_view_edit_the_profile_of_and_manage_the_sessions_of_their_own_account(UserManagementAction action)
    {
        var ownUserId = Guid.NewGuid();
        var own = new ManagedUserSummary(ownUserId, RoleCodes.CompanyOwner, new HashSet<Guid>());
        UserManagementRules.EnsureCanManage(Owner(), ownUserId, own, action);
    }

    [Fact]
    public void A_manager_manages_users_of_its_own_stores_only()
    {
        UserManagementRules.EnsureCanManage(ManagerOfFirstStore(), Guid.NewGuid(), User(RoleCodes.CashierSalesman, FirstStoreId), UserManagementAction.EditProfile);
        Assert.Equal(ErrorKind.Forbidden, KindOf(() =>
            UserManagementRules.EnsureCanManage(ManagerOfFirstStore(), Guid.NewGuid(), User(RoleCodes.CashierSalesman, SecondStoreId), UserManagementAction.EditProfile)));
    }

    [Fact]
    public void A_manager_cannot_manage_another_manager()
    {
        Assert.Equal(ErrorKind.Forbidden, KindOf(() =>
            UserManagementRules.EnsureCanManage(ManagerOfFirstStore(), Guid.NewGuid(), User(RoleCodes.StoreManager, FirstStoreId), UserManagementAction.Delete)));
    }

    [Fact]
    public void An_owner_edit_keeps_the_assignments_the_caller_asked_for()
    {
        var merged = UserManagementRules.MergeStoreAssignments(Owner(), [FirstStoreId, SecondStoreId], [SecondStoreId]);
        Assert.Equal([SecondStoreId], merged);
    }

    [Fact]
    public void A_manager_edit_cannot_remove_or_add_assignments_outside_its_own_stores()
    {
        var thirdStoreId = Guid.NewGuid();
        // The user works in the manager's store and in a store the manager does not see. The manager asks for [first, third].
        var merged = UserManagementRules.MergeStoreAssignments(ManagerOfFirstStore(), [FirstStoreId, SecondStoreId], [FirstStoreId, thirdStoreId]);
        Assert.Contains(SecondStoreId, merged);   // outside the manager's scope: stays
        Assert.Contains(FirstStoreId, merged);
        Assert.DoesNotContain(thirdStoreId, merged); // outside the manager's scope: not added
    }
}
