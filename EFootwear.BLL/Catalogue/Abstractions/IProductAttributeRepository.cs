using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>Bulk reassignment of a product classification (docx 4.6). Implemented in EFootwear.DAL/Catalogue/Repositories/ProductAttributeRepository.cs.</summary>
public interface IProductAttributeRepository
{
    Task<PagedResult<ProductAttributeRowResponse>> ListProductsWithValueAsync(string fieldType, Guid oldValueId, PageRequest pageRequest, CancellationToken cancellationToken);

    /// <summary>Changes every product whose field has the old value. Returns the products changed and a note for each product that had to be skipped.</summary>
    Task<(int ChangedCount, IReadOnlyList<string> SkippedProducts)> ApplyAsync(string fieldType, Guid oldValueId, Guid newValueId, CancellationToken cancellationToken);
}
