using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// Refuses to start if the app connects as a superuser or a role that bypasses row-level security.
/// Such a role ignores every tenant rule in the database, so a company could then read another's data.
/// </summary>
public sealed class DatabaseRoleGuard(NpgsqlDataSource dataSource, ILogger<DatabaseRoleGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select current_user, rolsuper, rolbypassrls from pg_roles where rolname = current_user", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var (roleName, isSuperuser, bypassesRowLevelSecurity) =
            (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));

        if (isSuperuser || bypassesRowLevelSecurity)
        {
            logger.LogCritical("Database role {RoleName} is a superuser or bypasses row-level security. Refusing to start.", roleName);
            throw new InvalidOperationException(
                $"Database role '{roleName}' is a superuser or bypasses row-level security. " +
                "Connect as a login role that is a member of pos_app (see db/create_app_login.sql).");
        }

        logger.LogInformation("Database role {RoleName} verified: not a superuser, row-level security applies.", roleName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
