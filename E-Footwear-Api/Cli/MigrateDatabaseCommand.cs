using EFootwear.DAL.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace E_Footwear_Api.Cli;

/// <summary>
/// Operator command. Applies the numbered scripts of db/migrations, or rolls them back.
///
///   dotnet run --project E-Footwear-Api -- migrate                    apply every migration that is not applied yet
///   dotnet run --project E-Footwear-Api -- migrate --down 0100        roll back migration 0100 and every later one, newest first
///   dotnet run --project E-Footwear-Api -- migrate --directory path  use another folder than db/migrations
///
/// Uses ConnectionStrings:Admin (the database owner). The baseline db/schema_v7_uuid.sql must already be installed.
/// </summary>
public static class MigrateDatabaseCommand
{
    private const string DownOption = "--down";
    private const string DirectoryOption = "--directory";
    private const string DefaultMigrationsFolder = "db/migrations";

    public const string Usage = "Usage: migrate [--down <version>] [--directory <path>]";

    public static async Task<int> RunAsync(string[] arguments, IConfiguration configuration)
    {
        var ownerConnectionString = configuration.GetConnectionString(CreatePlatformAdministratorCommand.AdminConnectionStringName);
        if (string.IsNullOrEmpty(ownerConnectionString))
        {
            Console.Error.WriteLine($"ConnectionStrings:{CreatePlatformAdministratorCommand.AdminConnectionStringName} is not set.");
            return 1;
        }

        string? downFromVersion = null;
        string? directory = null;
        for (var index = 1; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case DownOption when index + 1 < arguments.Length: downFromVersion = arguments[++index]; break;
                case DirectoryOption when index + 1 < arguments.Length: directory = arguments[++index]; break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        directory ??= RepositoryFolderLocator.FindUpwards(DefaultMigrationsFolder);
        if (directory is null)
        {
            Console.Error.WriteLine($"Could not find {DefaultMigrationsFolder}. Run from inside the repository or pass {DirectoryOption}.");
            return 1;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
        var runner = new DatabaseMigrationRunner(ownerConnectionString, loggerFactory.CreateLogger<DatabaseMigrationRunner>());

        var changedVersions = downFromVersion is null
            ? await runner.ApplyPendingMigrationsAsync(directory, CancellationToken.None)
            : await runner.RevertFromVersionAsync(directory, downFromVersion, CancellationToken.None);

        Console.WriteLine(changedVersions.Count == 0
            ? "Nothing to do."
            : $"{(downFromVersion is null ? "Applied" : "Reverted")}: {string.Join(", ", changedVersions)}");
        return 0;
    }
}
