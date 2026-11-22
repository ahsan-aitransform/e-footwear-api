namespace E_Footwear_Api.Cli;

/// <summary>The operator commands: <c>dotnet run --project E-Footwear-Api -- &lt;command&gt; ...</c>. They run without starting the web server.</summary>
public static class CommandNames
{
    public const string CreatePlatformAdministrator = "create-platform-admin";
    public const string MigrateDatabase = "migrate";
    public const string GeneratePermissionCodes = "generate-permission-codes";

    public static bool IsCommand(string name) =>
        name is CreatePlatformAdministrator or MigrateDatabase or GeneratePermissionCodes;
}
