using System.Linq.Expressions;

namespace EFootwear.BLL.Common;

/// <summary>Paging and sorting for any <see cref="IQueryable{T}"/> (works with EF Core without referencing it).</summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Sorts by the requested field when it is allowed, otherwise by <paramref name="defaultKeySelector"/>; then takes
    /// the requested page. The default key must be unique (for example the id) so pages never overlap.
    /// Count the rows with <c>CountAsync</c> on the unpaged query to fill <see cref="PagedResult{TItem}.TotalItemCount"/>.
    /// </summary>
    public static IQueryable<TEntity> ApplySortingAndPaging<TEntity, TDefaultKey>(
        this IQueryable<TEntity> source, PageRequest pageRequest, SortableFields<TEntity> sortableFields,
        Expression<Func<TEntity, TDefaultKey>> defaultKeySelector)
    {
        var request = pageRequest.Normalized();
        var ordered = sortableFields.TryOrder(source, request.SortField, request.SortDirection, out var sorted)
            ? sorted.ThenBy(defaultKeySelector)
            : source.OrderBy(defaultKeySelector);
        return ordered.Skip(request.SkippedItemCount).Take(request.PageSize);
    }
}
