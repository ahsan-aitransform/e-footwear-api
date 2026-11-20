using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// One connection, one transaction and one EF Core context per use case, scoped to the request.
/// Before each use the scope is (re)applied to PostgreSQL with set_config(..., true):
///   app.company_id, app.user_id, app.store_ids, app.all_stores   (company users)
///   app.platform_admin                                            (platform administrators)
/// Row-level security reads those values, so every query through the context is limited to the
/// caller's company and stores. A platform administrator chooses the company per operation with
/// <see cref="UseCompany"/>; the scope is re-applied if it changes inside the same transaction.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource, RequestContext context) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlConnection? _conn;
    private NpgsqlTransaction? _tx;
    private AppDbContext? _db;
    private long? _companyOverride;
    private string? _appliedScope;

    /// <summary>Selects the company for the next statements. A company user can only be scoped to their own company.</summary>
    public void UseCompany(long companyId)
    {
        if (context.CompanyIdOrNull is { } own && own != companyId)
            throw new InvalidOperationException("A company user cannot act on another company.");
        _companyOverride = companyId;
    }

    /// <summary>The EF Core context bound to this request's transaction, with the caller's scope applied.</summary>
    public async Task<AppDbContext> DbAsync(CancellationToken ct)
    {
        var (conn, tx) = await ApplyScopeAsync(ct);
        if (_db is null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options;
            _db = new AppDbContext(options);
            await _db.Database.UseTransactionAsync(tx, ct);
        }
        return _db;
    }

    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> ApplyScopeAsync(CancellationToken ct)
    {
        if (_conn is null || _tx is null)
        {
            _conn = await dataSource.OpenConnectionAsync(ct);
            _tx = await _conn.BeginTransactionAsync(ct);
        }

        var company = context.CompanyIdOrNull ?? _companyOverride;
        var platform = context.IsPlatformAdmin;
        var user = context.UserIdOrNull;
        var allStores = platform || (context.IsAuthenticated && context.AllStores);
        var stores = context.IsAuthenticated && !platform ? string.Join(',', context.StoreIds) : "";

        var scope = $"{company}|{user}|{allStores}|{stores}|{platform}";
        if (scope == _appliedScope) return (_conn, _tx);

        // is_local = true: values live only until this transaction ends, so a pooled connection never leaks them.
        await using var cmd = new NpgsqlCommand(
            """
            select set_config('app.company_id',     $1, true),
                   set_config('app.user_id',        $2, true),
                   set_config('app.all_stores',     $3, true),
                   set_config('app.store_ids',      $4, true),
                   set_config('app.platform_admin', $5, true)
            """, _conn, _tx)
        {
            Parameters =
            {
                new() { Value = company?.ToString() ?? "" },
                new() { Value = user?.ToString() ?? "" },
                new() { Value = allStores ? "on" : "off" },
                new() { Value = stores },
                new() { Value = platform ? "on" : "off" },
            }
        };
        await cmd.ExecuteNonQueryAsync(ct);
        _appliedScope = scope;
        return (_conn, _tx);
    }

    /// <summary>Saves any pending entity changes, then commits the transaction.</summary>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_tx is null) return;
        if (_db is not null) await _db.SaveChangesAsync(ct);
        await _tx.CommitAsync(ct);
        await ReleaseAsync();
        _companyOverride = null;
        _appliedScope = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_tx is not null)
        {
            try { await _tx.RollbackAsync(); } catch { /* connection already broken */ }
        }
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        if (_db is not null) { await _db.DisposeAsync(); _db = null; }
        if (_tx is not null) { await _tx.DisposeAsync(); _tx = null; }
        if (_conn is not null) { await _conn.DisposeAsync(); _conn = null; }
        _appliedScope = null;
    }
}
