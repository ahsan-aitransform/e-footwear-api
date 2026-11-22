using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// One connection, one transaction and one EF Core context per use case, scoped to the request.
/// Before each use the scope is (re)applied to PostgreSQL with set_config(..., true) (see <see cref="SessionContextKeys"/>):
/// company, user, assigned stores and the platform-administrator flag.
/// Row-level security reads those values, so every query through the context is limited to the
/// caller's company and stores. A platform administrator chooses the company per operation with
/// <see cref="UseCompany"/>; the scope is re-applied if it changes inside the same transaction.
/// </summary>
public sealed class DatabaseSession(
    NpgsqlDataSource dataSource,
    RequestContext requestContext,
    SlowQueryInterceptor slowQueryInterceptor,
    ILoggerFactory loggerFactory)
    : IUnitOfWork, IAsyncDisposable
{
    private const string ApplyScopeSql = $"""
        select set_config('{SessionContextKeys.CompanyId}',            $1, true),
               set_config('{SessionContextKeys.UserId}',               $2, true),
               set_config('{SessionContextKeys.AllStores}',            $3, true),
               set_config('{SessionContextKeys.StoreIds}',             $4, true),
               set_config('{SessionContextKeys.PlatformAdministrator}', $5, true)
        """;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private ApplicationDatabaseContext? _databaseContext;
    private Guid? _companyOverride;
    private string? _appliedScope;

    /// <summary>Selects the company for the next statements. A company user can only be scoped to their own company.</summary>
    public void UseCompany(Guid companyId)
    {
        if (requestContext.CompanyIdOrNull is { } ownCompanyId && ownCompanyId != companyId)
            throw new InvalidOperationException("A company user cannot act on another company.");
        _companyOverride = companyId;
    }

    /// <summary>The EF Core context bound to this request's transaction, with the caller's scope applied.</summary>
    public async Task<ApplicationDatabaseContext> GetDatabaseContextAsync(CancellationToken cancellationToken)
    {
        var (connection, transaction) = await ApplyScopeAsync(cancellationToken);
        if (_databaseContext is null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDatabaseContext>()
                .UseNpgsql(connection)
                .UseLoggerFactory(loggerFactory)
                .AddInterceptors(slowQueryInterceptor)
                .Options;
            _databaseContext = new ApplicationDatabaseContext(options);
            await _databaseContext.Database.UseTransactionAsync(transaction, cancellationToken);
        }
        return _databaseContext;
    }

    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> ApplyScopeAsync(
        CancellationToken cancellationToken)
    {
        if (_connection is null || _transaction is null)
        {
            _connection = await dataSource.OpenConnectionAsync(cancellationToken);
            _transaction = await _connection.BeginTransactionAsync(cancellationToken);
        }

        var companyId = requestContext.CompanyIdOrNull ?? _companyOverride;
        var isPlatformAdministrator = requestContext.IsPlatformAdministrator;
        var userId = requestContext.UserIdOrNull;
        var hasAccessToAllStores = isPlatformAdministrator
            || (requestContext.IsAuthenticated && requestContext.HasAccessToAllStores);
        var assignedStoreIds = requestContext.IsAuthenticated && !isPlatformAdministrator
            ? string.Join(',', requestContext.AssignedStoreIds)
            : "";

        var scope = $"{companyId}|{userId}|{hasAccessToAllStores}|{assignedStoreIds}|{isPlatformAdministrator}";
        if (scope == _appliedScope) return (_connection, _transaction);

        // is_local = true: values live only until this transaction ends, so a pooled connection never leaks them.
        await using var command = new NpgsqlCommand(ApplyScopeSql, _connection, _transaction)
        {
            Parameters =
            {
                new() { Value = companyId?.ToString() ?? "" },
                new() { Value = userId?.ToString() ?? "" },
                new() { Value = hasAccessToAllStores ? SessionContextKeys.On : SessionContextKeys.Off },
                new() { Value = assignedStoreIds },
                new() { Value = isPlatformAdministrator ? SessionContextKeys.On : SessionContextKeys.Off },
            },
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
        _appliedScope = scope;
        return (_connection, _transaction);
    }

    /// <summary>Saves any pending entity changes, then commits the transaction.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;
        if (_databaseContext is not null) await _databaseContext.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        await ReleaseAsync();
        _companyOverride = null;
        _appliedScope = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            try { await _transaction.RollbackAsync(); } catch { /* connection already broken */ }
        }
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        if (_databaseContext is not null) { await _databaseContext.DisposeAsync(); _databaseContext = null; }
        if (_transaction is not null) { await _transaction.DisposeAsync(); _transaction = null; }
        if (_connection is not null) { await _connection.DisposeAsync(); _connection = null; }
        _appliedScope = null;
    }
}
