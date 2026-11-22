using System.Text.Json.Serialization;

namespace EFootwear.BLL.Common;

/// <summary>
/// One page of a list plus the numbers a grid needs to draw its pager. JSON (the frontend contract,
/// lib/types/pagination.ts): <c>{ items, page, pageSize, totalCount, totalPageCount }</c>.
/// </summary>
public sealed record PagedResult<TItem>(
    IReadOnlyList<TItem> Items,
    [property: JsonPropertyName("page")] int PageNumber,
    int PageSize,
    [property: JsonPropertyName("totalCount")] long TotalItemCount)
{
    public int TotalPageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItemCount / (double)PageSize);

    public static PagedResult<TItem> Empty(PageRequest pageRequest) =>
        new([], pageRequest.PageNumber, pageRequest.PageSize, 0);
}
