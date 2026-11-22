using EFootwear.BLL.Catalogue.Contracts;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>Reads the stock ledger (inv.stock_ledger) for the Product Ledger. Implemented in EFootwear.DAL/Catalogue/Repositories/ProductLedgerRepository.cs.</summary>
public interface IProductLedgerRepository
{
    /// <summary>The ledger of one product in one store; null when the product or the store does not exist for the caller.</summary>
    Task<ProductLedgerResponse?> GetLedgerAsync(Guid productId, Guid storeId, DateOnly fromDate, DateOnly toDate, bool detail, CancellationToken cancellationToken);
}
