namespace EFootwear.Tests.Infrastructure;

/// <summary>
/// Settings of the integration-test database. The only thing taken from the machine is the environment variable
/// <see cref="OwnerConnectionEnvironmentVariable"/>: a server-level owner connection string WITHOUT a database, for example
/// <c>Host=localhost;Port=5432;Username=postgres;Password=...</c>. The password is never written into the repository.
/// </summary>
public static class TestDatabaseSettings
{
    public const string OwnerConnectionEnvironmentVariable = "EFW_TEST_ADMIN";

    /// <summary>A login role used only by the tests (member of pos_app, no superuser, row-level security applies).</summary>
    public const string ApplicationLoginRoleName = "efw_test_login";

    /// <summary>Not a secret: the role exists only on a developer's own test cluster.</summary>
    public const string ApplicationLoginRolePassword = "efw-test-only-password";

    public const string DatabaseNamePrefix = "efw_it_";
    public const string MaintenanceDatabaseName = "postgres";

    /// <summary>The owner connection string from the environment, or a clear error when it is not set.</summary>
    public static string GetOwnerConnectionString() =>
        Environment.GetEnvironmentVariable(OwnerConnectionEnvironmentVariable) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"Set the environment variable {OwnerConnectionEnvironmentVariable} to a PostgreSQL owner connection string " +
                "without a database, e.g. Host=localhost;Port=5432;Username=postgres;Password=<password>. " +
                "The tests create and drop their own throwaway databases.");
}
