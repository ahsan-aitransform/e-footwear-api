using EFootwear.Application.Abstractions;
using EFootwear.Application.Trading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFootwear.Api.Controllers;

[ApiController, Authorize, Route("api/v1/sales")]
public sealed class SalesController(SalesService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery q, long? storeId, DateOnly? from, DateOnly? to, string? docType, string? status, long? customerId, long? salesmanId, long? invoiceNo, CancellationToken ct)
        => Ok(await svc.ListAsync(me.User, new SalesFilter(storeId, from, to, docType, status, customerId, salesmanId, invoiceNo), q, ct));

    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));
    [HttpGet("{id:long}/receipt")] public async Task<IActionResult> Receipt(long id, CancellationToken ct) => Ok(await svc.ReceiptAsync(me.User, id, ct));
    [HttpGet("{id:long}/returnable")] public async Task<IActionResult> Returnable(long id, CancellationToken ct) => Ok(await svc.ReturnableAsync(me.User, id, ct));
    [HttpGet("price-drifts")] public async Task<IActionResult> Drifts(CancellationToken ct) => Ok(await svc.DriftsAsync(me.User, ct));

    /// <summary>Post a sale. Sending the same <c>saleUuid</c> again (an offline retry) returns the stored invoice with 200 instead of creating a second one.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(SaleBody body, CancellationToken ct)
    {
        var r = await svc.CreateSaleAsync(me.User, body, ct);
        return r.Duplicate ? Ok(new { duplicate = true, invoice = r.Invoice }) : CreatedAtAction(nameof(Get), new { id = r.Invoice["id"] }, new { duplicate = false, invoice = r.Invoice });
    }

    [HttpPost("returns")]
    public async Task<IActionResult> Return(ReturnBody body, CancellationToken ct)
    {
        var r = await svc.CreateReturnAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id = r.Invoice["id"] }, new { invoice = r.Invoice });
    }

    [HttpPost("exchanges")] public async Task<IActionResult> Exchange(ExchangeBody body, CancellationToken ct) => Ok(await svc.ExchangeAsync(me.User, body, ct));
    [HttpPost("{id:long}/void")] public async Task<IActionResult> Void(long id, VoidBody body, CancellationToken ct) { await svc.VoidAsync(me.User, id, body, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/purchases")]
public sealed class PurchasesController(PurchaseService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery q, long? storeId, DateOnly? from, DateOnly? to, long? vendorId, string? docType, string? status, CancellationToken ct)
        => Ok(await svc.ListAsync(me.User, new PurchaseFilter(storeId, from, to, vendorId, docType, status, q.Search), q, ct));

    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseBody body, CancellationToken ct)
    {
        var inv = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id = inv["id"] }, inv);
    }

    [HttpPost("returns")]
    public async Task<IActionResult> Return(PurchaseReturnBody body, CancellationToken ct)
    {
        var inv = await svc.ReturnAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id = inv["id"] }, inv);
    }

    [HttpPost("{id:long}/void")] public async Task<IActionResult> Void(long id, VoidBody body, CancellationToken ct) { await svc.VoidAsync(me.User, id, body, ct); return NoContent(); }
}
