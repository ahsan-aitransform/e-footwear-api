namespace EFootwear.DAL.Catalogue;

/// <summary>
/// Collects the values of a hand-built SQL statement. <see cref="Add"/> returns the placeholder ({0}, {1} ...) to write into the SQL text; EF Core turns
/// each placeholder into a real database parameter (SqlQueryRaw / ExecuteSqlRawAsync), so values are never concatenated into the statement.
/// Only fixed text written in code (table names, column names from a whitelist) may be part of the SQL string itself.
/// </summary>
internal sealed class SqlParameterList
{
    private readonly List<object> values = [];

    public string Add(object value)
    {
        values.Add(value);
        return "{" + (values.Count - 1) + "}";
    }

    public object[] ToArray() => [.. values];
}
