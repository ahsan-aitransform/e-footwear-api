using Microsoft.Extensions.Hosting;
using Npgsql;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// Refuses to start if the app connects as a superuser or a role that bypasses row-level security.
/// Such a role ignores every tenant rule in the database, so a company could then read another's data.
/// </summary>
public sealed class DatabaseRoleGuard(NpgsqlDataSource dataSource) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "select current_user, rolsuper, rolbypassrls from pg_roles where rolname = current_user", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var (name, isSuper, bypass) = (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));

        if (isSuper || bypass)
            throw new InvalidOperationException(
                $"Database role '{name}' is a superuser or bypasses row-level security. " +
                "Connect as a login role that is a member of pos_app (see db/create_app_login.sql).");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
