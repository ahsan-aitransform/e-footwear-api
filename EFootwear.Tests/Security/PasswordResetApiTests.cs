using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>Forgot password: administrator-issued single-use tokens, the anonymous request that never reveals anything, and completion.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PasswordResetApiTests(ApiTestEnvironment environment)
{
    private const string RequestUrl = "/api/v1/auth/password-reset/request";
    private const string CompleteUrl = "/api/v1/auth/password-reset/complete";

    private static async Task<string> IssueTokenAsync(SecurityTestCompany company, Guid userId)
    {
        var response = await company.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(userId, "password-reset-token"), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await SecurityTestCompany.ReadAsync(response);
        Assert.True(body.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        return body.GetProperty("resetToken").GetString()!;
    }

    [Fact]
    public async Task A_token_issued_by_an_administrator_sets_a_new_password_once_and_ends_the_sessions()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        var token = await IssueTokenAsync(company, cashier.UserId);
        var anonymous = company.CreateAnonymousClient();
        const string newPassword = "Brand#New123";

        var complete = await anonymous.PostAsJsonAsync(CompleteUrl, new { resetToken = token, newPassword });

        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashierClient.GetAsync("/api/v1/auth/me")).StatusCode);
        var (client, mustChange) = await company.LoginAsync(cashier.Email, newPassword);
        Assert.False(mustChange);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => company.LoginAsync(cashier.Email, SecurityTestCompany.PasswordAfterFirstSignIn));

        var reuse = await anonymous.PostAsJsonAsync(CompleteUrl, new { resetToken = token, newPassword = "Other#Pass456" });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task The_token_is_stored_only_as_a_hash_and_a_garbage_token_is_refused()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var token = await IssueTokenAsync(company, cashier.UserId);
        var secret = token[(token.IndexOf('.') + 1)..];
        var anonymous = company.CreateAnonymousClient();

        Assert.Equal(0L, await company.QueryScalarAsync<long>(
            "select count(*) from sec.password_reset_tokens where company_id = $1 and token_hash = $2", company.CompanyId, secret));
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from sec.password_reset_tokens where company_id = $1 and token_hash = $2",
            company.CompanyId, EFootwear.DAL.Security.SessionTokenHasher.Hash(secret)));

        foreach (var garbage in new[] { "", "not-a-token", $"{company.CompanyId}.wrong-secret", $"{Guid.NewGuid()}.{secret}" })
        {
            var response = await anonymous.PostAsJsonAsync(CompleteUrl, new { resetToken = garbage, newPassword = "Valid#Pass123" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_and_the_token_stays_usable()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var token = await IssueTokenAsync(company, cashier.UserId);
        var anonymous = company.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync(CompleteUrl, new { resetToken = token, newPassword = "weak" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync(CompleteUrl, new { resetToken = token, newPassword = "Strong#Pass123" })).StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var token = await IssueTokenAsync(company, cashier.UserId);
        await environment.Database.ExecuteAsOwnerAsync(
            "update sec.password_reset_tokens set expires_at = now() - interval '1 minute', created_at = now() - interval '2 hours' where company_id = $1",
            company.CompanyId);

        var response = await company.CreateAnonymousClient().PostAsJsonAsync(CompleteUrl, new { resetToken = token, newPassword = "Strong#Pass123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_hourly_limit_of_the_auth_policy_stops_more_tokens()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var limit = await company.QueryScalarAsync<int>("select reset_max_per_hour from sec.auth_policy where company_id = $1", company.CompanyId);

        for (var index = 0; index < limit; index++) await IssueTokenAsync(company, cashier.UserId);
        var tooMany = await company.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(cashier.UserId, "password-reset-token"), null);

        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Equal("TOO_MANY_REQUESTS", (await SecurityTestCompany.ReadAsync(tooMany)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_anonymous_request_answers_202_for_known_and_unknown_emails_alike_and_sends_nothing()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        var anonymous = company.CreateAnonymousClient();

        var known = await anonymous.PostAsJsonAsync(RequestUrl, new { email = cashier.Email });
        var unknown = await anonymous.PostAsJsonAsync(RequestUrl, new { email = $"nobody.{Guid.NewGuid():N}@example.test" });
        var empty = await anonymous.PostAsJsonAsync(RequestUrl, new { email = "" });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, empty.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from sec.login_attempts where company_id = $1 and user_id = $2 and outcome = 'RESET_REQUESTED'",
            company.CompanyId, cashier.UserId));
    }

    [Fact]
    public async Task Issuing_a_token_needs_the_reset_permissions_and_cannot_target_the_owner_or_another_company()
    {
        var companyA = await SecurityTestCompany.CreateAsync(environment);
        var companyB = await SecurityTestCompany.CreateAsync(environment);
        var cashierOfA = await companyA.CreateUserAsync(RoleCodes.CashierSalesman);
        var ownerId = await companyA.QueryScalarAsync<Guid>("select id from sec.users where company_id = $1 and role_code = 'COMPANY_OWNER'", companyA.CompanyId);
        var (_, noPermissionClient) = await companyA.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermissionClient.PostAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId, "password-reset-token"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(cashierOfA.UserId, "password-reset-token"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await companyA.OwnerClient.PostAsync(SecurityTestCompany.UserUrl(ownerId, "password-reset-token"), null)).StatusCode);
    }
}
