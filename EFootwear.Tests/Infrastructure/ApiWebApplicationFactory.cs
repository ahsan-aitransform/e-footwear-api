using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EFootwear.Tests.Infrastructure;

/// <summary>Hosts the real API in memory, connected to a throwaway database as the (non-superuser) test login role.</summary>
public sealed class ApiWebApplicationFactory(string applicationLoginConnectionString) : WebApplicationFactory<Program>
{
    private const string TestingEnvironmentName = "Testing";
    private const int UnlimitedLoginAttempts = 1_000_000;

    /// <summary>Where the API stores uploaded files during the tests (a folder of its own under the temp directory).</summary>
    public string FileStorageDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "efootwear-test-storage", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FileStorage:RootDirectory", FileStorageDirectory);
        builder.UseEnvironment(TestingEnvironmentName);   // not Development: user secrets of the developer must not leak in

        // UseSetting (not ConfigureAppConfiguration): Program.Run reads the connection string while it builds the services,
        // before ConfigureAppConfiguration callbacks would be applied.
        builder.UseSetting("ConnectionStrings:Default", applicationLoginConnectionString);
        // every in-memory request comes from the same address, so the production sign-in limit would block the tests
        builder.UseSetting("RateLimiting:LoginAttemptsPerWindow", UnlimitedLoginAttempts.ToString());
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }
}
