using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>The legal and regional profile: time zone, currency, NTN, STRN, rounding. Changed through a database function.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CompanyProfileApiTests(ApiTestEnvironment environment)
{
    private static object Profile(string timezone = "Asia/Karachi", string currencyCode = "pkr", string roundingMode = "NEAREST",
                                  long? expectedRowVersion = null) => new
    {
        legalName = "Bata Pakistan (Private) Limited",
        ntn = "1234567-8",
        strn = (string?)null,
        timezone,
        currencyCode,
        roundingMode,
        priceBandBasis = "NET",
        expectedRowVersion,
    };

    [Fact]
    public async Task The_profile_starts_with_the_schema_defaults_and_can_be_changed_by_the_owner()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        var before = await (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyProfile)).ReadSuccessJsonAsync();
        Assert.Equal("PKR", before.GetProperty("currencyCode").GetString());
        Assert.Equal("Asia/Karachi", before.GetProperty("timezone").GetString());
        Assert.Equal("FLOOR", before.GetProperty("roundingMode").GetString());
        Assert.Equal("STANDARD", before.GetProperty("planCode").GetString());

        var after = await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile("Asia/Dubai"))).ReadSuccessJsonAsync();
        Assert.Equal("Asia/Dubai", after.GetProperty("timezone").GetString());
        Assert.Equal("PKR", after.GetProperty("currencyCode").GetString());
        Assert.Equal("NEAREST", after.GetProperty("roundingMode").GetString());
        Assert.Equal("NET", after.GetProperty("priceBandBasis").GetString());
        Assert.Equal("1234567-8", after.GetProperty("ntn").GetString());
        Assert.True(after.GetProperty("rowVersion").GetInt64() > before.GetProperty("rowVersion").GetInt64());
    }

    [Fact]
    public async Task Invalid_profile_values_are_refused_and_a_stale_version_is_a_conflict()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var before = await (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyProfile)).ReadSuccessJsonAsync();
        var version = before.GetProperty("rowVersion").GetInt64();

        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile(currencyCode: "RUPEE")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile(roundingMode: "UP")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile(timezone: "Mars/Phobos")))
            .AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DATABASE_RULE_VIOLATION");

        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile(expectedRowVersion: version))).ReadSuccessJsonAsync();
        await (await scenario.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile(expectedRowVersion: version)))
            .AssertProblemAsync(HttpStatusCode.Conflict, "CONCURRENT_UPDATE");
    }

    [Fact]
    public async Task The_profile_needs_its_permission_and_never_reaches_another_company()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        var viewer = await first.CreateUserClientAsync("STORE_MANAGER", [first.Company.FirstStoreId], PermissionCodes.Settings.CompanyProfile.View);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(SettingsUrls.CompanyProfile)).StatusCode);
        await (await viewer.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile())).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        // there is no way to name another company: each owner only ever reads and changes their own row
        await (await second.OwnerClient.PutAsJsonAsync(SettingsUrls.CompanyProfile, Profile("Asia/Tokyo"))).ReadSuccessJsonAsync();
        var firstProfile = await (await first.OwnerClient.GetAsync(SettingsUrls.CompanyProfile)).ReadSuccessJsonAsync();
        Assert.Equal("Asia/Karachi", firstProfile.GetProperty("timezone").GetString());
        await (await first.PlatformClient.GetAsync(SettingsUrls.CompanyProfile)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
