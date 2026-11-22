using EFootwear.DAL.Security;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace E_Footwear_Api.Cli;

/// <summary>
/// Operator command. Creates the first platform administrator, because the API cannot create one by itself.
///
///   PLATFORM_ADMIN_PASSWORD='...' dotnet run --project E-Footwear-Api -- create-platform-admin &lt;username&gt; "&lt;full name&gt;"
///
/// Uses ConnectionStrings:Admin (the database owner), from user secrets or environment variables.
/// The password comes from an environment variable so it never appears in shell history.
/// </summary>
public static class CreatePlatformAdministratorCommand
{
    public const string PasswordEnvironmentVariable = "PLATFORM_ADMIN_PASSWORD";
    public const string AdminConnectionStringName = "Admin";
    private const int MinimumArgumentCount = 3;
    private const int MinimumPasswordLength = 8;

    public static readonly string Usage =
        "Usage: create-platform-admin <username> \"<full name>\"\n" +
        $"Set {PasswordEnvironmentVariable} to the temporary password ({MinimumPasswordLength}+ characters). Set ConnectionStrings:{AdminConnectionStringName} to the owner connection.";

    public static async Task<int> RunAsync(string[] arguments, IConfiguration configuration)
    {
        if (arguments.Length < MinimumArgumentCount)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var username = arguments[1].Trim();
        var fullName = arguments[2].Trim();
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable) ?? "";
        var ownerConnectionString = configuration.GetConnectionString(AdminConnectionStringName);

        if (string.IsNullOrEmpty(ownerConnectionString))
        {
            Console.Error.WriteLine($"ConnectionStrings:{AdminConnectionStringName} is not set.");
            return 1;
        }
        if (password.Length < MinimumPasswordLength || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var passwordHash = new Argon2PasswordHasher().Hash(password);

        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "insert into platform.admins (username, password_hash, full_name) values (@username, @passwordHash, @fullName) returning id",
            connection);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        command.Parameters.AddWithValue("fullName", fullName);

        try
        {
            var administratorId = await command.ExecuteScalarAsync();
            Console.WriteLine($"Platform administrator '{username}' created (id {administratorId}). Sign in at /api/v1/platform/auth/login.");
            return 0;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            Console.Error.WriteLine($"A platform administrator named '{username}' already exists.");
            return 1;
        }
    }
}
