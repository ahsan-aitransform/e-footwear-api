using System.Text.RegularExpressions;
using Npgsql;

namespace EFootwear.Infrastructure.Persistence;

/// <summary>
/// Minimal SQL-first migration runner. Applies <c>NNNN_name.sql</c> files in order and records them in
/// <c>public.schema_migrations</c>. Needs a connection that owns the database (it creates schemas, roles and RLS policies).
/// A script that already contains its own top-level <c>BEGIN; … COMMIT;</c> (the v6 baseline does) is run as it is; any other
/// script is wrapped in one transaction here.
/// </summary>
public static partial class Migrator
{
    public static async Task<IReadOnlyList<string>> RunAsync(string adminConnectionString, string migrationsDir, string? apiRole, CancellationToken ct = default)
    {
        if (!Directory.Exists(migrationsDir)) throw new DirectoryNotFoundException($"Migrations folder not found: {migrationsDir}");
        var files = Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();

        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);
        await Exec(conn, null, "create table if not exists public.schema_migrations (version text primary key, applied_at timestamptz not null default now())", ct);

        // The v6 baseline GRANTs to pos_app (lines ~4104) before it creates that role (~4110), so a fresh cluster fails.
        // Roles are cluster-wide; creating it first is idempotent and identical to what the baseline does afterwards.
        await Exec(conn, null, "do $$ begin if not exists (select 1 from pg_roles where rolname = 'pos_app') then create role pos_app nologin nobypassrls; end if; end $$", ct);

        var applied = new List<string>();
        foreach (var file in files)
        {
            var version = Path.GetFileNameWithoutExtension(file);
            await using (var chk = new NpgsqlCommand("select 1 from public.schema_migrations where version = @v", conn))
            {
                chk.Parameters.AddWithValue("v", version);
                if (await chk.ExecuteScalarAsync(ct) is not null) continue;
            }

            var sql = await File.ReadAllTextAsync(file, ct);
            if (OwnTransaction().IsMatch(sql))
            {
                await Exec(conn, null, sql, ct);
                await Exec(conn, null, $"insert into public.schema_migrations (version) values ('{version.Replace("'", "''")}')", ct);
            }
            else
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await Exec(conn, tx, sql, ct);
                await Exec(conn, tx, $"insert into public.schema_migrations (version) values ('{version.Replace("'", "''")}')", ct);
                await tx.CommitAsync(ct);
            }
            applied.Add(version);
        }

        // The API logs in as its own role; the schema's NOLOGIN role pos_app carries every privilege and policy.
        if (!string.IsNullOrWhiteSpace(apiRole))
        {
            if (!Identifier().IsMatch(apiRole)) throw new ArgumentException("Database:ApiRole must be a plain identifier.", nameof(apiRole));
            await Exec(conn, null, $"grant pos_app to \"{apiRole}\"", ct);
        }
        return applied;
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 600 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex(@"^\s*BEGIN;\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex OwnTransaction();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
