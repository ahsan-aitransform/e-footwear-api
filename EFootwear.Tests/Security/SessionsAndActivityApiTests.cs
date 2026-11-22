using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>Own sessions, sessions of others, sign-in history, the Users Activity feed and the quick-action toolbar.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class SessionsAndActivityApiTests(ApiTestEnvironment environment)
{
    [Fact]
    public async Task A_person_sees_their_live_sessions_with_the_current_one_marked_and_can_end_the_others()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, firstClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        var (secondClient, _) = await company.LoginAsync(cashier.Email, SecurityTestCompany.PasswordAfterFirstSignIn);

        var sessions = (await SecurityTestCompany.ReadAsync(await secondClient.GetAsync(SecurityTestCompany.AccountUrl("sessions")))).EnumerateArray().ToList();

        Assert.Equal(2, sessions.Count);
        Assert.Equal(1, sessions.Count(session => session.GetProperty("isCurrentSession").GetBoolean()));
        Assert.DoesNotContain("tokenHash", sessions[0].ToString(), StringComparison.OrdinalIgnoreCase);

        var ended = await secondClient.DeleteAsync(SecurityTestCompany.AccountUrl("sessions/others"));
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        Assert.Equal(1, (await SecurityTestCompany.ReadAsync(ended)).GetProperty("endedCount").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_person_can_end_one_of_their_own_sessions_but_not_somebody_elses()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (_, firstClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        var (_, otherClient) = await company.CreateSignedInUserAsync(RoleCodes.Accountant);
        var otherSessions = await SecurityTestCompany.ReadAsync(await otherClient.GetAsync(SecurityTestCompany.AccountUrl("sessions")));
        var otherSessionId = otherSessions[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.DeleteAsync(SecurityTestCompany.AccountUrl("sessions/" + otherSessionId))).StatusCode);

        var ownSessionId = (await SecurityTestCompany.ReadAsync(await firstClient.GetAsync(SecurityTestCompany.AccountUrl("sessions"))))[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await firstClient.DeleteAsync(SecurityTestCompany.AccountUrl("sessions/" + ownSessionId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_lists_and_ends_the_sessions_of_a_user_and_it_is_logged()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        var sessions = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId, "sessions")));
        var sessionId = sessions[0].GetProperty("id").GetGuid();
        var ended = await company.OwnerClient.DeleteAsync(SecurityTestCompany.UserUrl(cashier.UserId, "sessions/" + sessionId));

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and action = $2", company.CompanyId, SecurityActivityActions.SessionRevoked));
        Assert.Equal(HttpStatusCode.NotFound,
            (await company.OwnerClient.DeleteAsync(SecurityTestCompany.UserUrl(cashier.UserId, "sessions/" + sessionId))).StatusCode);
    }

    [Fact]
    public async Task The_login_history_shows_own_events_to_everyone_and_all_events_only_with_the_permissions()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        var own = await SecurityTestCompany.ReadAsync(await cashierClient.GetAsync(SecurityTestCompany.AccountUrl("login-history")));
        Assert.Contains(own.EnumerateArray(), entry => entry.GetProperty("outcome").GetString() == "SUCCESS");
        Assert.All(own.EnumerateArray(), entry => Assert.Equal(cashier.UserId, entry.GetProperty("userId").GetGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, (await cashierClient.GetAsync(SecurityTestCompany.UsersUrl("login-history"))).StatusCode);

        var all = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("login-history")));
        Assert.True(all.GetArrayLength() >= 2);
        var oneUser = await SecurityTestCompany.ReadAsync(
            await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl($"login-history?userId={cashier.UserId}")));
        Assert.All(oneUser.EnumerateArray(), entry => Assert.Equal(cashier.UserId, entry.GetProperty("userId").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("login-history?from=2025-01-01T00:00:00Z&to=2020-01-01T00:00:00Z"))).StatusCode);
    }

    [Fact]
    public async Task The_activity_feed_has_data_changes_with_before_and_after_values_and_sign_in_events_and_never_a_password_hash()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var rowVersion = await SecurityTestCompany.ReadRowVersionAsync(company.OwnerClient, cashier.UserId);
        await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId), new
        {
            email = cashier.Email, fullName = "Edited For Activity", roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { company.FirstStoreId }, rowVersion,
        });

        var page = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(
            SecurityTestCompany.UsersUrl("activity?entryKind=BUSINESS_CHANGE&pageSize=100&search=sec.users")));

        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.True(page.GetProperty("totalCount").GetInt64() >= 2);
        var update = page.GetProperty("items").EnumerateArray().First(entry =>
            entry.GetProperty("action").GetString() == "UPDATE" && entry.GetProperty("entityId").GetString() == cashier.UserId.ToString());
        Assert.Contains("Edited For Activity", update.GetProperty("newValuesJson").GetString());
        Assert.Contains("Test CASHIER_SALESMAN", update.GetProperty("oldValuesJson").GetString());
        Assert.DoesNotContain("password_hash", page.ToString(), StringComparison.OrdinalIgnoreCase);

        var signIns = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?entryKind=SIGN_IN")));
        Assert.True(signIns.GetProperty("totalCount").GetInt64() >= 1);
        Assert.All(signIns.GetProperty("items").EnumerateArray(), entry => Assert.Equal("SIGN_IN", entry.GetProperty("entryKind").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?entryKind=NOPE"))).StatusCode);
    }

    [Fact]
    public async Task The_activity_feed_is_limited_to_the_own_company_and_to_the_own_stores_for_a_manager()
    {
        var companyA = await SecurityTestCompany.CreateAsync(environment);
        var companyB = await SecurityTestCompany.CreateAsync(environment);
        var secondStoreId = await companyA.CreateStoreAsync();
        var (_, managerClient) = await companyA.CreateSignedInUserAsync(RoleCodes.StoreManager, null, PermissionCodes.UserAccount.UsersActivity.View);
        var otherStoreCashier = await companyA.CreateUserAsync(RoleCodes.CashierSalesman, secondStoreId);

        var ownerView = await companyA.OwnerClient.GetStringAsync(SecurityTestCompany.UsersUrl("activity?pageSize=200"));
        Assert.Contains(otherStoreCashier.Email, ownerView);
        var managerView = await managerClient.GetStringAsync(SecurityTestCompany.UsersUrl("activity?pageSize=200"));
        Assert.DoesNotContain(otherStoreCashier.Email, managerView);
        var otherCompanyView = await companyB.OwnerClient.GetStringAsync(SecurityTestCompany.UsersUrl("activity?pageSize=200"));
        Assert.DoesNotContain(otherStoreCashier.Email, otherCompanyView);
    }

    [Fact]
    public async Task Paging_and_sorting_of_the_activity_feed_follow_the_query_string_and_ignore_unknown_sort_fields()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        for (var index = 0; index < 3; index++) await company.CreateUserAsync(RoleCodes.CashierSalesman);

        var firstPage = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?page=1&pageSize=2")));
        var secondPage = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?page=2&pageSize=2")));
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(2, secondPage.GetProperty("page").GetInt32());
        var firstIds = firstPage.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("entryId").GetGuid()).ToHashSet();
        Assert.All(secondPage.GetProperty("items").EnumerateArray(), entry => Assert.DoesNotContain(entry.GetProperty("entryId").GetGuid(), firstIds));

        // the names the frontend sends
        var longNames = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(
            SecurityTestCompany.UsersUrl("activity?pageNumber=2&pageSize=2&sortField=occurredAt&sortDirection=Ascending&searchText=sec.users")));
        Assert.Equal(2, longNames.GetProperty("page").GetInt32());
        Assert.Equal(2, longNames.GetProperty("pageSize").GetInt32());
        Assert.All(longNames.GetProperty("items").EnumerateArray(), entry => Assert.Equal("sec.users", entry.GetProperty("entityType").GetString()));
        var ascending = (await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(
            SecurityTestCompany.UsersUrl("activity?pageSize=50&sortField=occurredAt&sortDirection=Ascending")))).GetProperty("items")
            .EnumerateArray().Select(entry => entry.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(ascending.Order().ToList(), ascending);
        var descending = (await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(
            SecurityTestCompany.UsersUrl("activity?pageSize=50&sortField=occurredAt&sortDirection=Descending")))).GetProperty("items")
            .EnumerateArray().Select(entry => entry.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(descending.OrderDescending().ToList(), descending);

        Assert.Equal(HttpStatusCode.OK, (await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?sort=-userFullName"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await company.OwnerClient.GetAsync(SecurityTestCompany.UsersUrl("activity?sort=password_hash"))).StatusCode);
    }

    [Fact]
    public async Task The_quick_action_toolbar_is_set_by_an_administrator_and_read_by_the_user_without_a_permission()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        var replace = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "quick-actions"),
            new { actionCodes = new[] { QuickActionCodes.NewSale, QuickActionCodes.ViewInvoices } });
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);

        var own = await SecurityTestCompany.ReadAsync(await cashierClient.GetAsync(SecurityTestCompany.AccountUrl("quick-actions")));
        Assert.Equal([QuickActionCodes.NewSale, QuickActionCodes.ViewInvoices], own.GetProperty("actionCodes").EnumerateArray().Select(code => code.GetString()!));
        Assert.Equal(HttpStatusCode.BadRequest, (await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "quick-actions"),
            new { actionCodes = new[] { "MAKE_COFFEE" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashierClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "quick-actions"),
            new { actionCodes = new[] { QuickActionCodes.Backup } })).StatusCode);
    }
}
