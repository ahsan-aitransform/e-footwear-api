using System.Linq.Expressions;

namespace EFootwear.BLL.Common;

/// <summary>
/// The columns a list may be sorted by. A client names a field ("name", "createdAt"); this map turns the name into a
/// real expression, so a client can never sort by something that was not offered (and never injects a column name).
/// Build one per list in the repository, then call <see cref="QueryableExtensions.ApplySortingAndPaging"/>.
///
///   private static readonly SortableFields&lt;Product&gt; ProductSortableFields = new SortableFields&lt;Product&gt;()
///       .Allow("name", product => product.Name)
///       .Allow("createdAt", product => product.CreatedAt);
/// </summary>
public sealed class SortableFields<TEntity>
{
    private readonly Dictionary<string, Func<IQueryable<TEntity>, SortDirection, IOrderedQueryable<TEntity>>> orderings =
        new(StringComparer.OrdinalIgnoreCase);

    public SortableFields<TEntity> Allow<TKey>(string fieldName, Expression<Func<TEntity, TKey>> keySelector)
    {
        orderings[fieldName] = (source, direction) => direction == SortDirection.Descending
            ? source.OrderByDescending(keySelector)
            : source.OrderBy(keySelector);
        return this;
    }

    public bool TryOrder(IQueryable<TEntity> source, string? fieldName, SortDirection direction,
                         out IOrderedQueryable<TEntity> ordered)
    {
        if (fieldName is not null && orderings.TryGetValue(fieldName, out var ordering))
        {
            ordered = ordering(source, direction);
            return true;
        }
        ordered = default!;
        return false;
    }
}
