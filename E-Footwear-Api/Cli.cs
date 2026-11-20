using EFootwear.Infrastructure.Persistence;
using EFootwear.Infrastructure.Provisioning;
using EFootwear.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace EFootwear.Api;

/// <summary>Operator commands. Secrets come from environment variables so they never appear in shell history or source control.</summary>
public static class Cli
{
    public const string Usage = """
        dotnet run -- migrate [folder]
            Applies db/migrations using ConnectionStrings:Admin (the database OWNER) and grants pos_app to Database:ApiRole.
        dotnet run -- create-company <name> <firstStoreCode> <firstStoreName> <ownerUserName> <ownerFullName>
            Registers a company with its first store and owner. Owner password comes from OWNER_PASSWORD (8+ characters).
            Set OWNER_KEEP_PASSWORD=1 to skip the forced change at first sign-in.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddUserSecrets(typeof(Cli).Assembly, optional: true)   // developer defaults ...
            .AddEnvironmentVariables()                              // ... which an explicit environment variable always overrides
            .Build();
        var admin = cfg.GetConnectionString("Admin") ?? throw new InvalidOperationException("ConnectionStrings:Admin is not set.");

        switch (args[0])
        {
            case "migrate":
            {
                var applied = await Migrator.RunAsync(admin, args.Length > 1 ? args[1] : FindMigrationsDir(), cfg["Database:ApiRole"]);
                Console.WriteLine(applied.Count == 0 ? "Database is up to date." : "Applied: " + string.Join(", ", applied));
                return 0;
            }
            case "create-company":
            {
                var pw = Environment.GetEnvironmentVariable("OWNER_PASSWORD");
                if (args.Length < 6 || string.IsNullOrEmpty(pw)) { Console.Error.WriteLine(Usage); return 1; }
                var r = await CompanyProvisioner.CreateAsync(admin,
                    new NewCompany(args[1], args[2], args[3], args[4], args[5], pw, OwnerMustChangePassword: Environment.GetEnvironmentVariable("OWNER_KEEP_PASSWORD") != "1"),
                    new Argon2PasswordHasher());
                Console.WriteLine($"Company {r.CompanyId} created. First store id {r.FirstStoreId}, owner user id {r.OwnerUserId}. Sign in with company '{args[1]}' or '{r.CompanyId}'.");
                return 0;
            }
        }
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static string FindMigrationsDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "db", "migrations");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("Could not find db/migrations. Pass the folder as the second argument.");
    }
}
