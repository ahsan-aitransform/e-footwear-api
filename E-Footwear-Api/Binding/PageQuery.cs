using EFootwear.BLL.Common;
using Microsoft.AspNetCore.Mvc;
using SortDirectionKind = EFootwear.BLL.Common.SortDirection;

namespace E_Footwear_Api.Binding;

/// <summary>
/// The paging query string every list endpoint accepts. The frontend (lib/constants/paginationDefaults.ts) sends
/// <c>?pageNumber=2&amp;pageSize=25&amp;sortField=name&amp;sortDirection=Descending&amp;searchText=bata</c>. The shorter spelling
/// <c>?page=2&amp;sort=-name&amp;search=bata</c> (a leading minus in <c>sort</c> means descending) is accepted as well, and the long
/// names win when both are present. A controller takes <c>[FromQuery] PageQuery pageQuery</c> and passes
/// <see cref="ToPageRequest"/> to the service. The sort column is only a request: the service sorts through a
/// <c>SortableFields</c> map, so a client can never sort by a column the endpoint did not allow.
/// </summary>
public sealed class PageQuery
{
    private const char DescendingPrefix = '-';

    [FromQuery(Name = "pageNumber")]
    public int? PageNumber { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int PageSize { get; init; } = PaginationDefaults.DefaultPageSize;

    [FromQuery(Name = "sortField")]
    public string? SortField { get; init; }

    [FromQuery(Name = "sortDirection")]
    public string? SortDirection { get; init; }

    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "searchText")]
    public string? SearchText { get; init; }

    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    public PageRequest ToPageRequest()
    {
        var (sortField, direction) = ResolveSort();
        return new PageRequest(PageNumber ?? Page ?? PaginationDefaults.FirstPageNumber, PageSize, sortField, direction,
                               SearchText ?? Search).Normalized();
    }

    private (string? Field, SortDirectionKind Direction) ResolveSort()
    {
        if (!string.IsNullOrWhiteSpace(SortField))
        {
            var isDescending = string.Equals((SortDirection ?? "").Trim(), nameof(SortDirectionKind.Descending), StringComparison.OrdinalIgnoreCase);
            return (SortField.Trim(), isDescending ? SortDirectionKind.Descending : SortDirectionKind.Ascending);
        }

        var shortSort = (Sort ?? "").Trim();
        var shortIsDescending = shortSort.StartsWith(DescendingPrefix);
        var shortField = shortIsDescending ? shortSort[1..] : shortSort;
        return (string.IsNullOrEmpty(shortField) ? null : shortField,
                shortIsDescending ? SortDirectionKind.Descending : SortDirectionKind.Ascending);
    }
}
