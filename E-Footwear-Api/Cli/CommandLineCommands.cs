using Microsoft.Extensions.Configuration;

namespace E_Footwear_Api.Cli;

/// <summary>Runs the operator commands (see <see cref="CommandNames"/>). Configuration comes from user secrets and environment variables.</summary>
public static class CommandLineCommands
{
    public static bool IsCommand(string[] arguments) => arguments.Length > 0 && CommandNames.IsCommand(arguments[0]);

    public static Task<int> RunAsync(string[] arguments)
    {
        var configuration = WebApplication.CreateBuilder(arguments).Configuration;
        return arguments[0] switch
        {
            CommandNames.CreatePlatformAdministrator => CreatePlatformAdministratorCommand.RunAsync(arguments, configuration),
            CommandNames.MigrateDatabase => MigrateDatabaseCommand.RunAsync(arguments, configuration),
            CommandNames.GeneratePermissionCodes => Task.FromResult(GeneratePermissionCodesCommand.Run(arguments)),
            _ => throw new InvalidOperationException($"Unknown command {arguments[0]}."),
        };
    }
}
