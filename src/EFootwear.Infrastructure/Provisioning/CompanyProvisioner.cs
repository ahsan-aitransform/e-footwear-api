using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Npgsql;

namespace EFootwear.Infrastructure.Provisioning;

public sealed record NewCompany(string Name, string FirstStoreCode, string FirstStoreName, string OwnerUserName, string OwnerFullName,
                                string OwnerPassword, string Plan = "STANDARD", bool OwnerMustChangePassword = true);

public sealed record ProvisionedCompany(long CompanyId, long FirstStoreId, long OwnerUserId);

/// <summary>
/// Registers a company. The v6 schema has no platform-administrator table, so this is an operator action run with the
/// database OWNER connection (the CLI command "create-company"), not an API endpoint. Database triggers create the head-office
/// store, channels, chart of accounts, the "Cash Sale" customer and the auth policy; each store gets its own accounts.
/// </summary>
public static class CompanyProvisioner
{
    public static async Task<ProvisionedCompany> CreateAsync(string ownerConnectionString, NewCompany c, IPasswordHasher hasher, CancellationToken ct = default)
    {
        new Check().NotBlank(c.Name, "name").NotBlank(c.FirstStoreCode, "firstStoreCode").NotBlank(c.FirstStoreName, "firstStoreName")
            .NotBlank(c.OwnerUserName, "ownerUserName").NotBlank(c.OwnerFullName, "ownerFullName")
            .Require(c.OwnerPassword.Length >= 8, "ownerPassword", "Owner password needs at least 8 characters.").ThrowIfAny();

        await using var conn = new NpgsqlConnection(ownerConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // v6 seeds no platform.plans row although companies.plan_code defaults to 'STANDARD' (FK). Create it when missing.
        // The limits are NOT a commercial decision: they are set high enough to never bind until the owner defines real plans.
        await Exec(conn, tx, "insert into platform.plans (code, name, max_stores, max_users) values (@p, @p, 1000, 10000) on conflict (code) do nothing", ct, ("p", c.Plan));

        var companyId = await Scalar<long>(conn, tx, "insert into platform.companies (name, plan_code) values (@n, @p) returning id",
            ct, ("n", c.Name.Trim()), ("p", c.Plan));

        // The company trigger switched the scope to the new company; make that explicit for the rest of this transaction.
        await Exec(conn, tx, "select set_config('app.company_id', @c, true), set_config('app.all_stores', 'on', true)", ct, ("c", companyId.ToString()));

        var storeId = await Scalar<long>(conn, tx, "insert into core.stores (company_id, code, name) values (@c, @code, @name) returning id",
            ct, ("c", companyId), ("code", c.FirstStoreCode.Trim()), ("name", c.FirstStoreName.Trim()));

        var userId = await Scalar<long>(conn, tx,
            @"insert into sec.users (company_id, username, password_hash, full_name, role_code, all_stores, must_change_password)
              values (@c, @u, @h, @f, 'COMPANY_OWNER', true, @m) returning id",
            ct, ("c", companyId), ("u", c.OwnerUserName.Trim()), ("h", hasher.Hash(c.OwnerPassword)), ("f", c.OwnerFullName.Trim()), ("m", c.OwnerMustChangePassword));

        await tx.CommitAsync(ct);
        return new ProvisionedCompany(companyId, storeId, userId);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string, object)[] p)
    {
        await using var cmd = Build(conn, tx, sql, p);
        return (T)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string, object)[] p)
    {
        await using var cmd = Build(conn, tx, sql, p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand Build(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, (string, object)[] p)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
        return cmd;
    }
}
