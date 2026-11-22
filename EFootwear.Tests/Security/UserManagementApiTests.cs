using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>Users Account (docx 1.1, 1.3): details, edit, block, reset password, delete and the protections around them.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class UserManagementApiTests(ApiTestEnvironment environment)
{
    private const string ErrorCodeProperty = "errorCode";

    [Fact]
    public async Task Details_return_the_user_with_stores_and_a_row_version()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        var response = await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await SecurityTestCompany.ReadAsync(response);
        Assert.Equal(cashier.Email, body.GetProperty("email").GetString());
        Assert.Equal(RoleCodes.CashierSalesman, body.GetProperty("roleCode").GetString());
        Assert.Equal(company.FirstStoreId, body.GetProperty("storeIds")[0].GetGuid());
        Assert.True(body.GetProperty("rowVersion").GetInt64() >= 1);
    }

    [Fact]
    public async Task The_user_list_shows_sign_in_information_and_never_a_password()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        var response = await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl());

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", text.Replace("mustChangePassword", ""), StringComparison.OrdinalIgnoreCase);
        var rows = System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray().ToList();
        var ownerRow = rows.Single(row => row.GetProperty("roleCode").GetString() == RoleCodes.CompanyOwner);
        Assert.True(ownerRow.GetProperty("isSignedInNow").GetBoolean());
        Assert.NotNull(ownerRow.GetProperty("lastSignInAt").GetString());
        var cashierRow = rows.Single(row => row.GetProperty("id").GetGuid() == cashier.UserId);
        Assert.False(cashierRow.GetProperty("isSignedInNow").GetBoolean());
    }

    [Fact]
    public async Task Editing_a_user_changes_name_and_email_and_a_stale_row_version_is_refused_with_409()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var rowVersion = await SecurityTestCompany.ReadRowVersionAsync(company.OwnerClient, cashier.UserId);
        var newEmail = $"renamed.{Guid.NewGuid():N}@example.test";

        var first = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = newEmail, fullName = "Renamed Cashier", roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { company.FirstStoreId }, rowVersion,
        });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var details = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId)));
        Assert.Equal(newEmail, details.GetProperty("email").GetString());
        Assert.Equal("Renamed Cashier", details.GetProperty("fullName").GetString());

        var stale = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = newEmail, fullName = "Second Edit", roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { company.FirstStoreId }, rowVersion,
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENT_UPDATE", (await SecurityTestCompany.ReadAsync(stale)).GetProperty(ErrorCodeProperty).GetString());
    }

    [Fact]
    public async Task An_email_already_in_use_is_refused_and_a_role_the_caller_may_not_create_is_forbidden()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var other = await company.CreateUserAsync(RoleCodes.Accountant);
        var rowVersion = await SecurityTestCompany.ReadRowVersionAsync(company.OwnerClient, cashier.UserId);

        var duplicate = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = other.Email, fullName = "X", roleCode = RoleCodes.CashierSalesman, storeIds = new[] { company.FirstStoreId }, rowVersion,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var promote = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = cashier.Email, fullName = "X", roleCode = RoleCodes.CompanyOwner, storeIds = Array.Empty<Guid>(), rowVersion,
        });
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
    }

    [Fact]
    public async Task Changing_the_role_to_accountant_and_the_store_is_saved_and_logged()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var secondStoreId = await company.CreateStoreAsync();
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var rowVersion = await SecurityTestCompany.ReadRowVersionAsync(company.OwnerClient, cashier.UserId);

        var response = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = cashier.Email, fullName = "Now Accountant", roleCode = RoleCodes.Accountant, storeIds = new[] { secondStoreId }, rowVersion,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var details = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId)));
        Assert.Equal(RoleCodes.Accountant, details.GetProperty("roleCode").GetString());
        Assert.Equal(secondStoreId, details.GetProperty("storeIds")[0].GetGuid());
        var loggedChanges = await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and action = $2 and entity_id = $3",
            company.CompanyId, SecurityActivityActions.StoreAssignmentsChanged, cashier.UserId.ToString());
        Assert.Equal(1L, loggedChanges);
    }

    [Fact]
    public async Task A_blocked_user_loses_the_session_and_cannot_sign_in_until_unblocked()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman, null,
            PermissionCodes.UserAccount.ChangePassword.View);
        Assert.Equal(HttpStatusCode.OK, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);

        var block = await company.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(cashier.UserId, "block"), null);

        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() =>
            company.LoginAsync(cashier.Email, SecurityTestCompany.PasswordAfterFirstSignIn));
        Assert.Contains("401", refused.Message);

        var unblock = await company.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(cashier.UserId, "unblock"), null);
        Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        var (client, _) = await company.LoginAsync(cashier.Email, SecurityTestCompany.PasswordAfterFirstSignIn);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_reset_forces_a_password_change_ends_sessions_and_the_old_password_stops_working()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        const string temporaryPassword = "Reset#Pass123";

        var reset = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "reset-password"),
            new { temporaryPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => company.LoginAsync(cashier.Email, SecurityTestCompany.PasswordAfterFirstSignIn));
        var (_, mustChange) = await company.LoginAsync(cashier.Email, temporaryPassword);
        Assert.True(mustChange);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletterspassword")]
    [InlineData("1234567890")]
    public async Task A_weak_temporary_password_is_refused_on_creation_and_on_reset(string weakPassword)
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        var reset = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "reset-password"),
            new { temporaryPassword = weakPassword });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);

        var create = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.UsersUrl(), new
        {
            email = $"weak.{Guid.NewGuid():N}@example.test", fullName = "Weak", roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { company.FirstStoreId }, password = weakPassword,
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task A_deleted_user_disappears_cannot_sign_in_and_loses_the_session()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        var delete = await company.OwnerClient.DeleteAsync(SecurityTestCompany.UserUrl(cashier.UserId));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId))).StatusCode);
        var list = await company.OwnerClient.GetStringAsync(SecurityTestCompany.UsersUrl());
        Assert.DoesNotContain(cashier.Email, list);
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and action = $2 and entity_id = $3",
            company.CompanyId, SecurityActivityActions.UserDeleted, cashier.UserId.ToString()));
        // soft delete: the row and its history stay
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from sec.users where company_id = $1 and id = $2 and is_deleted", company.CompanyId, cashier.UserId));
    }

    [Fact]
    public async Task Nobody_can_block_delete_or_reset_their_own_account_and_the_owner_is_protected_from_others()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var ownerId = await company.QueryScalarAsync<Guid>(
            "select id from sec.users where company_id = $1 and role_code = 'COMPANY_OWNER'", company.CompanyId);

        foreach (var request in new[]
        {
            (Method: HttpMethod.Post, Url: SecurityTestCompany.UserUrl(ownerId, "block")),
            (Method: HttpMethod.Delete, Url: SecurityTestCompany.UserUrl(ownerId)),
        })
        {
            var response = await company.OwnerClient.SendAsync(new HttpRequestMessage(request.Method, request.Url));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var resetOwn = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.UserUrl(ownerId, "reset-password"),
            new { temporaryPassword = "Another#Pass1" });
        Assert.Equal(HttpStatusCode.Forbidden, resetOwn.StatusCode);

        // a manager holding every user permission still cannot touch the owner
        var (_, managerClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.UserAccount.ViewUsers.Edit, PermissionCodes.UserAccount.ViewUsers.Delete,
            PermissionCodes.RestrictedOperations.Administration.UserBlock, PermissionCodes.RestrictedOperations.Administration.UserPasswordReset);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.PostAsync(SecurityTestCompany.UserUrl(ownerId, "block"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.DeleteAsync(SecurityTestCompany.UserUrl(ownerId))).StatusCode);
    }

    [Fact]
    public async Task A_manager_cannot_manage_a_user_of_another_store_but_can_manage_one_of_its_own()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var secondStoreId = await company.CreateStoreAsync();
        var (_, managerClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.UserAccount.ViewUsers.Edit, PermissionCodes.UserAccount.ViewUsers.Delete,
            PermissionCodes.RestrictedOperations.Administration.UserBlock);
        var ownStoreCashier = await company.CreateUserAsync(RoleCodes.CashierSalesman, company.FirstStoreId);
        var otherStoreCashier = await company.CreateUserAsync(RoleCodes.CashierSalesman, secondStoreId);

        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync(SecurityTestCompany.UserUrl(ownStoreCashier.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await managerClient.PostAsync(SecurityTestCompany.UserUrl(ownStoreCashier.UserId, "block"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.GetAsync(SecurityTestCompany.UserUrl(otherStoreCashier.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.DeleteAsync(SecurityTestCompany.UserUrl(otherStoreCashier.UserId))).StatusCode);
    }

    [Fact]
    public async Task A_company_cannot_reach_a_user_of_another_company()
    {
        var companyA = await SecurityTestCompany.CreateAsync(environment);
        var companyB = await SecurityTestCompany.CreateAsync(environment);
        var cashierOfA = await companyA.CreateUserAsync(RoleCodes.CashierSalesman);

        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId, "block"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId, "permissions"))).StatusCode);
        Assert.DoesNotContain(cashierOfA.Email, await companyB.OwnerClient.GetStringAsync(SecurityTestCompany.UsersUrl()));
    }

    [Fact]
    public async Task Without_the_permission_every_user_action_is_refused_with_403()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var target = await company.CreateUserAsync(RoleCodes.Accountant);
        var (_, noPermissionClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        // Bodies are valid on purpose: a malformed body is answered 400 by model validation before the permission check.
        var validUpdateBody = new { email = target.Email, fullName = "X", roleCode = RoleCodes.Accountant, storeIds = new[] { company.FirstStoreId }, rowVersion = 1L };
        var requests = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, SecurityTestCompany.UserUrl(target.UserId), null),
            (HttpMethod.Put, SecurityTestCompany.UserUrl(target.UserId), validUpdateBody),
            (HttpMethod.Delete, SecurityTestCompany.UserUrl(target.UserId), null),
            (HttpMethod.Post, SecurityTestCompany.UserUrl(target.UserId, "block"), null),
            (HttpMethod.Post, SecurityTestCompany.UserUrl(target.UserId, "unblock"), null),
            (HttpMethod.Post, SecurityTestCompany.UserUrl(target.UserId, "reset-password"), new { temporaryPassword = "Valid#Pass123" }),
            (HttpMethod.Post, SecurityTestCompany.UserUrl(target.UserId, "password-reset-token"), null),
            (HttpMethod.Get, SecurityTestCompany.UserUrl(target.UserId, "permissions"), null),
            (HttpMethod.Put, SecurityTestCompany.UserUrl(target.UserId, "permissions"), new { permissionCodes = Array.Empty<string>() }),
            (HttpMethod.Get, SecurityTestCompany.UserUrl(target.UserId, "sessions"), null),
            (HttpMethod.Get, SecurityTestCompany.UserUrl(target.UserId, "quick-actions"), null),
            (HttpMethod.Put, SecurityTestCompany.UserUrl(target.UserId, "quick-actions"), new { actionCodes = Array.Empty<string>() }),
            (HttpMethod.Get, SecurityTestCompany.UsersUrl("activity"), null),
            (HttpMethod.Get, SecurityTestCompany.UsersUrl("login-history"), null),
        };
        foreach (var (method, url, body) in requests)
        {
            var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await noPermissionClient.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {url} returned {(int)response.StatusCode}, expected 403");
        }
    }

    [Fact]
    public async Task A_platform_administrator_is_refused_on_the_company_only_user_actions()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        Assert.Equal(HttpStatusCode.Forbidden, (await company.PlatformClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.PlatformClient.DeleteAsync(SecurityTestCompany.UserUrl(cashier.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.PlatformClient.GetAsync(SecurityTestCompany.UsersUrl("activity"))).StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_with_chosen_permissions_stores_exactly_those_and_refuses_codes_the_creator_does_not_hold()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        string[] chosen = [PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.UserAccount.UsersActivity.View];

        var created = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.UsersUrl(), new
        {
            email = $"chosen.{Guid.NewGuid():N}@example.test", fullName = "Chosen", roleCode = RoleCodes.StoreManager,
            storeIds = new[] { company.FirstStoreId }, password = TestScenarioBuilder.TemporaryPassword,
            permissionCodes = chosen, quickActionCodes = new[] { QuickActionCodes.NewSale, QuickActionCodes.Dashboard },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await SecurityTestCompany.ReadAsync(created)).GetProperty("id").GetGuid();

        var granted = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(userId, "permissions")));
        Assert.Equal(chosen.Order().ToArray(), granted.GetProperty("grantedPermissionCodes").EnumerateArray().Select(code => code.GetString()!).ToArray());
        var toolbar = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(userId, "quick-actions")));
        Assert.Equal([QuickActionCodes.NewSale, QuickActionCodes.Dashboard], toolbar.GetProperty("actionCodes").EnumerateArray().Select(code => code.GetString()!));

        // a manager who holds only a few permissions cannot hand out more than it holds
        var (_, managerClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.UserAccount.CreateUser.View, PermissionCodes.UserAccount.CreateUser.Add);
        var escalation = await managerClient.PostAsJsonAsync(SecurityTestCompany.UsersUrl(), new
        {
            email = $"esc.{Guid.NewGuid():N}@example.test", fullName = "Escalation", roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { company.FirstStoreId }, password = TestScenarioBuilder.TemporaryPassword,
            permissionCodes = new[] { PermissionCodes.Invoice.NewSale.Add },
        });
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);
    }
}
