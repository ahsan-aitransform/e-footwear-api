using System.Data;
using System.Text.Json;
using Dapper;
using EFootwear.Application.Abstractions;
using Npgsql;

namespace EFootwear.Infrastructure.Persistence;

/// <summary>A connection + transaction pair with small Dapper helpers. All SQL is parameterised.</summary>
public sealed class Db(NpgsqlConnection conn, NpgsqlTransaction tx)
{
    private const int TimeoutSeconds = 60;

    public NpgsqlConnection Connection => conn;
    public NpgsqlTransaction Transaction => tx;

    private CommandDefinition Cmd(string sql, object? p, CancellationToken ct)
        => new(sql, p, tx, TimeoutSeconds, cancellationToken: ct);

    public async Task<int> ExecAsync(string sql, object? p = null, CancellationToken ct = default)
        => await conn.ExecuteAsync(Cmd(sql, p, ct));

    public async Task<T?> ScalarAsync<T>(string sql, object? p = null, CancellationToken ct = default)
        => await conn.ExecuteScalarAsync<T>(Cmd(sql, p, ct));

    public async Task<List<T>> QueryAsync<T>(string sql, object? p = null, CancellationToken ct = default)
        => (await conn.QueryAsync<T>(Cmd(sql, p, ct))).AsList();

    /// <summary>Rows with camelCase keys. A column named <c>x_json</c> is parsed and exposed as <c>x</c>.</summary>
    public async Task<List<Row>> RowsAsync(string sql, object? p = null, CancellationToken ct = default)
    {
        var raw = await conn.QueryAsync(Cmd(sql, p, ct));
        var list = new List<Row>();
        foreach (IDictionary<string, object> d in raw) list.Add(ToRow(d));
        return list;
    }

    public async Task<Row?> RowAsync(string sql, object? p = null, CancellationToken ct = default)
        => (await RowsAsync(sql, p, ct)).FirstOrDefault();

    public async Task<Page<Row>> PageAsync(string selectFromWhere, string orderBy, object? p, PageQuery q, CancellationToken ct)
    {
        var dyn = new DynamicParameters(p);
        dyn.Add("lim", q.Limit);
        dyn.Add("off", q.Offset);
        var total = await ScalarAsync<long>($"select count(*) from ({selectFromWhere}) t", dyn, ct);
        var rows = await RowsAsync($"{selectFromWhere} order by {orderBy} limit @lim offset @off", dyn, ct);
        return new Page<Row>(rows, (int)total, Math.Max(q.Page, 1), q.Limit);
    }

    public static string Json(object? o) => JsonSerializer.Serialize(o, JsonOpts);
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    internal static Row ToRow(IDictionary<string, object> d)
    {
        var r = new Row();
        foreach (var (k, v) in d)
        {
            var isJson = k.EndsWith("_json", StringComparison.Ordinal);
            var key = Camel(isJson ? k[..^5] : k);
            object? val = v is DBNull ? null : v;
            if (isJson) val = val is string s ? JsonDocument.Parse(s).RootElement.Clone() : null;
            r[key] = val is System.Net.IPAddress ip ? ip.ToString() : val;
        }
        return r;
    }

    public static string Camel(string snake)
    {
        if (!snake.Contains('_')) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts[0] + string.Concat(parts.Skip(1).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }
}

/// <summary>Dapper type handlers for <see cref="DateOnly"/> (PostgreSQL <c>date</c>).</summary>
public sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value) { parameter.DbType = DbType.Date; parameter.Value = value; }
    public override DateOnly Parse(object value) => value switch { DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => DateOnly.Parse(value.ToString()!) };
}

public sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly? value) { parameter.DbType = DbType.Date; parameter.Value = value.HasValue ? value.Value : DBNull.Value; }
    public override DateOnly? Parse(object value) => value is null or DBNull ? null : value switch { DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => DateOnly.Parse(value.ToString()!) };
}
