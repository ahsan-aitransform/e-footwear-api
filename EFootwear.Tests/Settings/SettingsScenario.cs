using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>
/// A company with its owner signed in, plus the platform administrator who created it. Users with an exact permission set are created
/// on demand. Every scenario makes its own company, so tests never see each other's data.
/// </summary>
public sealed class SettingsScenario
{
    private readonly ApiTestEnvironment environment;

    private SettingsScenario(ApiTestEnvironment environment, HttpClient platformClient, TestCompany company, HttpClient ownerClient)
    {
        this.environment = environment;
        PlatformClient = platformClient;
        Company = company;
        OwnerClient = ownerClient;
    }

    public HttpClient PlatformClient { get; }
    public TestCompany Company { get; }
    public HttpClient OwnerClient { get; }

    public static async Task<SettingsScenario> CreateAsync(ApiTestEnvironment environment)
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        return new SettingsScenario(environment, platformClient, company, ownerClient);
    }

    /// <summary>Creates a user of the company who has EXACTLY these permission codes, and signs in as that user.</summary>
    public async Task<HttpClient> CreateUserClientAsync(string roleCode, IReadOnlyList<Guid> storeIds, params string[] permissionCodes)
    {
        var user = await environment.Scenarios.CreateUserAsync(OwnerClient, null, roleCode, storeIds);
        await environment.Scenarios.SetUserPermissionsAsync(Company.CompanyId, user.UserId, permissionCodes);
        return await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
    }

    public Task<Guid> CreateStoreAsync(string code) =>
        environment.Scenarios.CreateStoreAsync(PlatformClient, Company.CompanyId, code);
}
