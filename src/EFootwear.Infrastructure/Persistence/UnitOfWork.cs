using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Repositories;
using Npgsql;

namespace EFootwear.Infrastructure.Persistence;

public sealed class UnitOfWorkFactory(NpgsqlDataSource dataSource) : IUnitOfWorkFactory
{
    private const string Role = "pos_app";

    /// <summary>
    /// The transaction runs as the RLS-restricted role with the caller's scope (company, user, stores). The settings are
    /// transaction-local (<c>set_config(..., true)</c>), so a pooled connection can never leak one tenant's scope to another.
    /// </summary>
    public Task<IUnitOfWork> BeginAsync(UserContext user, CancellationToken ct = default)
        => BeginCoreAsync(user.CompanyId.ToString(), user.UserId.ToString(), string.Join(',', user.StoreIds.Where(s => s >= 0)),
                          user.AllStores ? "on" : "off", user.StoreId.ToString(), ct);

    public Task<IUnitOfWork> BeginForCompanyAsync(long companyId, CancellationToken ct = default)
        => BeginCoreAsync(companyId.ToString(), "", "", "off", "", ct);

    public Task<IUnitOfWork> BeginSystemAsync(long companyId, long? userId, CancellationToken ct = default)
        => BeginCoreAsync(companyId.ToString(), userId?.ToString() ?? "", "", "on", "", ct);

    public Task<IUnitOfWork> BeginAnonymousAsync(CancellationToken ct = default)
        => BeginCoreAsync("", "", "", "off", "", ct);

    private async Task<IUnitOfWork> BeginCoreAsync(string company, string user, string stores, string all, string store, CancellationToken ct)
    {
        var conn = await dataSource.OpenConnectionAsync(ct);
        NpgsqlTransaction? tx = null;
        try
        {
            tx = await conn.BeginTransactionAsync(ct);
            var db = new Db(conn, tx);
            await db.ExecAsync($"set local role {Role}", ct: ct);
            await db.ExecAsync(
                "select set_config('app.company_id', @c, true), set_config('app.user_id', @u, true), set_config('app.store_ids', @s, true), " +
                "set_config('app.all_stores', @a, true), set_config('app.store_id', @st, true)",
                new { c = company, u = user, s = stores, a = all, st = store }, ct);
            return new UnitOfWork(conn, tx, db);
        }
        catch
        {
            if (tx is not null) await tx.DisposeAsync();
            await conn.DisposeAsync();
            throw;
        }
    }
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;
    private bool _done;

    internal UnitOfWork(NpgsqlConnection conn, NpgsqlTransaction tx, Db db)
    {
        _conn = conn; _tx = tx;
        Auth = new AuthRepository(db);
        Users = new UserRepository(db);
        Stores = new StoreRepository(db);
        Masters = new MasterRepository(db);
        Parties = new PartyRepository(db);
        Catalog = new CatalogRepository(db);
        Stock = new StockRepository(db);
        Promos = new PromoRepository(db);
        Sales = new SalesRepository(db);
        Purchases = new PurchaseRepository(db);
        StockMoves = new StockMoves(db);
        Transfers = new TransferRepository(db);
        Inventory = new InventoryDocsRepository(db);
        Finance = new FinanceRepository(db);
    }

    public IAuthRepository Auth { get; }
    public IUserRepository Users { get; }
    public IStoreRepository Stores { get; }
    public IMasterRepository Masters { get; }
    public IPartyRepository Parties { get; }
    public ICatalogRepository Catalog { get; }
    public IStockRepository Stock { get; }
    public IPromoRepository Promos { get; }
    public ISalesRepository Sales { get; }
    public IPurchaseRepository Purchases { get; }
    public IStockMoves StockMoves { get; }
    public ITransferRepository Transfers { get; }
    public IInventoryDocsRepository Inventory { get; }
    public IFinanceRepository Finance { get; }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _tx.CommitAsync(ct);    // deferred triggers (balanced journals, stock rules) fire here
        _done = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_done) { try { await _tx.RollbackAsync(); } catch { /* connection already broken */ } }
        await _tx.DisposeAsync();
        await _conn.DisposeAsync();
    }
}
