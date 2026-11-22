namespace EFootwear.BLL.Common;

/// <summary>
/// What a list endpoint receives from the grid: which page, how many rows, how to sort and what to search for.
/// Bind it from the query string: <c>?pageNumber=2&amp;pageSize=50&amp;sortField=name&amp;sortDirection=Descending&amp;searchText=bata</c>.
/// Use <see cref="Normalized"/> before querying so a client can never ask for a negative or huge page.
/// </summary>
public sealed record PageRequest(
    int PageNumber = PaginationDefaults.FirstPageNumber,
    int PageSize = PaginationDefaults.DefaultPageSize,
    string? SortField = null,
    SortDirection SortDirection = SortDirection.Ascending,
    string? SearchText = null)
{
    /// <summary>The same request with the page number and page size forced into their allowed range.</summary>
    public PageRequest Normalized() => this with
    {
        PageNumber = Math.Max(PageNumber, PaginationDefaults.FirstPageNumber),
        PageSize = Math.Clamp(PageSize, 1, PaginationDefaults.MaximumPageSize),
        SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
    };

    /// <summary>How many rows to skip. Call on a normalized request.</summary>
    public int SkippedItemCount => (PageNumber - PaginationDefaults.FirstPageNumber) * PageSize;
}
