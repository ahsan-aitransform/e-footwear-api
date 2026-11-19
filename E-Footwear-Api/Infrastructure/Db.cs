using Npgsql;

namespace E_Footwear_Api.Infrastructure;

/// <summary>Company + store context for one request. Always derived server-side (Part 5.3, 31).</summary>
public sealed record TenantScope(long CompanyId, IReadOnlyList<long> StoreIds, bool AllStores);

public interface IDb
{
    /// <summary>Runs work in one transaction as the RLS-restricted app_user with tenant variables set.</summary>
    Task<T> InTenantAsync<T>(TenantScope scope, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct = default);

    /// <summary>Pre-authentication / platform work (no RLS role switch). Never call with client-chosen tenant ids.</summary>
    Task<T> InSystemAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct = default);
}

public sealed class Db(NpgsqlDataSource ds) : IDb
{
    public async Task<T> InTenantAsync<T>(TenantScope scope, Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand(
            "SET LOCAL ROLE app_user; " +
            "SELECT set_config('app.company_id', @c, true), set_config('app.store_ids', @s, true), set_config('app.all_stores', @a, true)",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("c", scope.CompanyId.ToString());
            cmd.Parameters.AddWithValue("s", string.Join(',', scope.StoreIds));
            cmd.Parameters.AddWithValue("a", scope.AllStores ? "on" : "off");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var result = await work(conn, tx);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<T> InSystemAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var result = await work(conn, tx);
        await tx.CommitAsync(ct);
        return result;
    }
}
