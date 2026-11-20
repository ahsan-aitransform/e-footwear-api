using EFootwear.DAL.Security;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace E_Footwear_Api.Cli;

/// <summary>
/// Operator command. Creates the first platform administrator, because the API cannot create one by itself.
///
///   PLATFORM_ADMIN_PASSWORD='...' dotnet run --project E-Footwear-Api -- create-platform-admin <username> "<full name>"
///
/// Uses ConnectionStrings:Admin (the database owner), from user secrets or environment variables.
/// The password comes from an environment variable so it never appears in shell history.
/// </summary>
public static class PlatformAdminCli
{
    public const string Usage =
        "Usage: create-platform-admin <username> \"<full name>\"\n" +
        "Set PLATFORM_ADMIN_PASSWORD to the temporary password (8+ characters). Set ConnectionStrings:Admin to the owner connection.";

    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var username = args[1].Trim();
        var fullName = args[2].Trim();
        var password = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD") ?? "";
        var adminConnection = config.GetConnectionString("Admin");

        if (string.IsNullOrEmpty(adminConnection))
        {
            Console.Error.WriteLine("ConnectionStrings:Admin is not set.");
            return 1;
        }
        if (password.Length < 8 || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var hash = new Argon2PasswordHasher().Hash(password);

        await using var conn = new NpgsqlConnection(adminConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into platform.admins (username, password_hash, full_name) values (@u, @h, @f) returning id", conn);
        cmd.Parameters.AddWithValue("u", username);
        cmd.Parameters.AddWithValue("h", hash);
        cmd.Parameters.AddWithValue("f", fullName);

        try
        {
            var id = await cmd.ExecuteScalarAsync();
            Console.WriteLine($"Platform administrator '{username}' created (id {id}). Sign in at /api/v1/platform/auth/login.");
            return 0;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            Console.Error.WriteLine($"A platform administrator named '{username}' already exists.");
            return 1;
        }
    }
}
