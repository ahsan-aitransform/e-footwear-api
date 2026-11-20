using EFootwear.Application.Abstractions;
using EFootwear.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFootwear.Api.Controllers;

[ApiController, Authorize, Route("api/v1/catalog")]
public sealed class CatalogController(CatalogService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] PageQuery q, long? categoryId, long? brandId, long? departmentId, string? status, long? storeId, CancellationToken ct)
        => Ok(await svc.ProductsAsync(me.User, new ProductFilter(categoryId, brandId, departmentId, status, storeId), q, ct));

    [HttpGet("products/{id:long}")] public async Task<IActionResult> Product(long id, CancellationToken ct) => Ok(await svc.ProductAsync(me.User, id, ct));

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct(ProductBody body, CancellationToken ct)
    {
        var id = await svc.CreateProductAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Product), new { id }, new { id });
    }

    [HttpPut("products/{id:long}")] public async Task<IActionResult> UpdateProduct(long id, ProductBody body, CancellationToken ct) { await svc.UpdateProductAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("products/{id:long}")] public async Task<IActionResult> DeleteProduct(long id, CancellationToken ct) { await svc.DeleteProductAsync(me.User, id, ct); return NoContent(); }

    [HttpPost("products/{id:long}/variants")] public async Task<IActionResult> AddVariant(long id, VariantBody body, CancellationToken ct) => Created("", new { id = await svc.AddVariantAsync(me.User, id, body, ct) });
    [HttpPut("variants/{id:long}")] public async Task<IActionResult> UpdateVariant(long id, VariantBody body, CancellationToken ct) { await svc.UpdateVariantAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("variants/{id:long}")] public async Task<IActionResult> DeleteVariant(long id, CancellationToken ct) { await svc.DeleteVariantAsync(me.User, id, ct); return NoContent(); }

    [HttpGet("variants/{id:long}/prices")] public async Task<IActionResult> Prices(long id, CancellationToken ct) => Ok(await svc.PricesAsync(me.User, id, ct));
    [HttpPut("variants/{id:long}/prices")] public async Task<IActionResult> SetPrices(long id, PriceBody body, CancellationToken ct) { await svc.SetPricesAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPut("variants/{id:long}/store-price")] public async Task<IActionResult> StorePrice(long id, StorePriceBody body, CancellationToken ct) { await svc.SetStorePriceAsync(me.User, id, body, ct); return NoContent(); }

    [HttpGet("price-history")]
    public async Task<IActionResult> History(long? variantId, Guid? batchId, int limit, CancellationToken ct) => Ok(await svc.PriceHistoryAsync(me.User, variantId, batchId, limit, ct));

    [HttpPost("prices/bulk")] public async Task<IActionResult> Bulk(BulkPriceBody body, CancellationToken ct) => Ok(await svc.BulkPricesAsync(me.User, body, ct));
    [HttpPost("prices/batches/{batchId:guid}/reverse")] public async Task<IActionResult> Reverse(Guid batchId, CancellationToken ct) => Ok(await svc.ReverseBatchAsync(me.User, batchId, ct));

    /// <summary>Scan a barcode at the counter: the article (with the price this store sells it at now and its stock) or a deal.</summary>
    [HttpGet("lookup")] public async Task<IActionResult> Lookup(string barcode, long? storeId, CancellationToken ct) => Ok(await svc.LookupAsync(me.User, barcode, storeId, ct));
    [HttpGet("search")] public async Task<IActionResult> Search(string q, long? storeId, int limit, CancellationToken ct) => Ok(await svc.SearchAsync(me.User, q, storeId, limit, ct));

    [HttpGet("price-bands")] public async Task<IActionResult> Bands(CancellationToken ct) => Ok(await svc.BandsAsync(me.User, ct));
    [HttpPost("price-bands")] public async Task<IActionResult> CreateBand(PriceBandBody body, CancellationToken ct) => Created("", new { id = await svc.CreateBandAsync(me.User, body, ct) });
    [HttpPut("price-bands/{id:long}")] public async Task<IActionResult> UpdateBand(long id, PriceBandBody body, CancellationToken ct) { await svc.UpdateBandAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("price-bands/{id:long}")] public async Task<IActionResult> DeleteBand(long id, CancellationToken ct) { await svc.DeleteBandAsync(me.User, id, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/stock")]
public sealed class StockController(StockService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Balances([FromQuery] PageQuery q, long? storeId, long? productId, long? categoryId, string? search, bool onlyPositive, CancellationToken ct)
        => Ok(await svc.BalancesAsync(me.User, storeId, productId, categoryId, search, onlyPositive, q, ct));

    [HttpGet("by-size/{productId:long}")] public async Task<IActionResult> BySize(long productId, CancellationToken ct) => Ok(await svc.BySizeAsync(me.User, productId, ct));

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] PageQuery q, long? variantId, long? storeId, DateOnly? from, DateOnly? to, string? docType, CancellationToken ct)
        => Ok(await svc.LedgerAsync(me.User, variantId, storeId, from, to, docType, q, ct));
}

[ApiController, Authorize, Route("api/v1/promotions")]
public sealed class PromotionsController(PromoService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Rules([FromQuery] PageQuery q, string? status, CancellationToken ct) => Ok(await svc.RulesAsync(me.User, status, q, ct));
    [HttpGet("{id:long}")] public async Task<IActionResult> Rule(long id, CancellationToken ct) => Ok(await svc.RuleAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(RuleBody body, CancellationToken ct)
    {
        var id = await svc.CreateRuleAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Rule), new { id }, new { id });
    }

    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, RuleBody body, CancellationToken ct) { await svc.UpdateRuleAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("{id:long}/publish")] public async Task<IActionResult> Publish(long id, CancellationToken ct) { await svc.PublishAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/cancel")] public async Task<IActionResult> Cancel(long id, CancellationToken ct) { await svc.CancelAsync(me.User, id, ct); return NoContent(); }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteRuleAsync(me.User, id, ct); return NoContent(); }

    [HttpPost("refresh")] public async Task<IActionResult> Refresh(CancellationToken ct) { await svc.RefreshAsync(me.User, ct); return NoContent(); }
    [HttpGet("channels")] public async Task<IActionResult> Channels(CancellationToken ct) => Ok(await svc.ChannelsAsync(me.User, ct));

    [HttpGet("conflicts")] public async Task<IActionResult> Conflicts(CancellationToken ct) => Ok(await svc.ConflictsAsync(me.User, ct));
    [HttpPost("conflicts/{id:long}/resolve")] public async Task<IActionResult> Resolve(long id, ResolveBody body, CancellationToken ct) { await svc.ResolveConflictAsync(me.User, id, body, ct); return NoContent(); }

    [HttpGet("deals")] public async Task<IActionResult> Deals([FromQuery] PageQuery q, CancellationToken ct) => Ok(await svc.BundlesAsync(me.User, q, ct));
    [HttpGet("deals/{id:long}")] public async Task<IActionResult> Deal(long id, CancellationToken ct) => Ok(await svc.BundleAsync(me.User, id, ct));
    [HttpPost("deals")] public async Task<IActionResult> CreateDeal(BundleBody body, CancellationToken ct) => Created("", new { id = await svc.CreateBundleAsync(me.User, body, ct) });
    [HttpPost("deals/{id:long}/publish")] public async Task<IActionResult> PublishDeal(long id, CancellationToken ct) { await svc.SetBundleStatusAsync(me.User, id, "PUBLISHED", ct); return NoContent(); }
    [HttpPost("deals/{id:long}/cancel")] public async Task<IActionResult> CancelDeal(long id, CancellationToken ct) { await svc.SetBundleStatusAsync(me.User, id, "CANCELLED", ct); return NoContent(); }
    [HttpDelete("deals/{id:long}")] public async Task<IActionResult> DeleteDeal(long id, CancellationToken ct) { await svc.DeleteBundleAsync(me.User, id, ct); return NoContent(); }
}
