namespace EFootwear.Tests.Infrastructure;

/// <summary>
/// Shared by every integration test class (xUnit collection fixture): ONE throwaway database and ONE in-memory API per
/// test run. Tests isolate themselves by creating their own companies, so they can share it.
/// </summary>
public sealed class ApiTestEnvironment : IAsyncLifetime
{
    public ThrowawayDatabase Database { get; private set; } = null!;
    public ApiWebApplicationFactory ApiFactory { get; private set; } = null!;
    public TestScenarioBuilder Scenarios { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Database = await ThrowawayDatabase.CreateAsync(applyMigrations: true);
        ApiFactory = new ApiWebApplicationFactory(Database.ApplicationLoginConnectionString);
        Scenarios = new TestScenarioBuilder(this);
    }

    public async Task DisposeAsync()
    {
        if (ApiFactory is not null) await ApiFactory.DisposeAsync();
        if (Database is not null) await Database.DisposeAsync();
    }
}
