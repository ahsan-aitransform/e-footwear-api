using EFootwear.Application.Abstractions;
using EFootwear.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFootwear.Api.Controllers;

[ApiController, Authorize, Route("api/v1/transfers")]
public sealed class TransfersController(TransferService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] PageQuery q, long? storeId, string? status, string? direction, CancellationToken ct) => Ok(await svc.ListAsync(me.User, new TransferFilter(storeId, status, direction), q, ct));
    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(TransferBody body, CancellationToken ct)
    {
        var id = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPost("{id:long}/scan-send")] public async Task<IActionResult> ScanSend(long id, ScanBody body, CancellationToken ct) => Ok(await svc.ScanAsync(me.User, id, "SEND", body, ct));
    [HttpPost("{id:long}/scan-receive")] public async Task<IActionResult> ScanReceive(long id, ScanBody body, CancellationToken ct) => Ok(await svc.ScanAsync(me.User, id, "RECEIVE", body, ct));
    [HttpPost("{id:long}/ship")] public async Task<IActionResult> Ship(long id, CancellationToken ct) { await svc.ShipAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/close")] public async Task<IActionResult> Close(long id, CancellationToken ct) { await svc.CloseAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/cancel")] public async Task<IActionResult> Cancel(long id, CancellationToken ct) { await svc.CancelAsync(me.User, id, ct); return NoContent(); }

    [HttpGet("loss-cases")] public async Task<IActionResult> Losses(string? status, CancellationToken ct) => Ok(await svc.LossCasesAsync(me.User, status, ct));
    [HttpPost("loss-cases/{id:long}/decide")] public async Task<IActionResult> Decide(long id, LossDecisionBody body, CancellationToken ct) { await svc.DecideLossAsync(me.User, id, body, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/inventory")]
public sealed class InventoryController(InventoryService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("documents")] public async Task<IActionResult> Documents([FromQuery] PageQuery q, long? storeId, string? docType, CancellationToken ct) => Ok(await svc.DocumentsAsync(me.User, storeId, docType, q, ct));
    [HttpGet("documents/{id:long}")] public async Task<IActionResult> Document(long id, CancellationToken ct) => Ok(await svc.DocumentAsync(me.User, id, ct));

    [HttpPost("adjustments")]
    public async Task<IActionResult> Adjust(AdjustBody body, CancellationToken ct)
    {
        var d = await svc.AdjustAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Document), new { id = d["id"] }, d);
    }

    [HttpPost("audits")] public async Task<IActionResult> Audit(AuditBody body, CancellationToken ct) => Ok(await svc.AuditAsync(me.User, body, ct));

    [HttpGet("claims")] public async Task<IActionResult> Claims([FromQuery] PageQuery q, long? storeId, string? status, CancellationToken ct) => Ok(await svc.ClaimsAsync(me.User, storeId, status, q, ct));
    [HttpGet("claims/{id:long}")] public async Task<IActionResult> Claim(long id, CancellationToken ct) => Ok(await svc.ClaimAsync(me.User, id, ct));

    [HttpPost("claims")]
    public async Task<IActionResult> RequestClaim(ClaimBody body, CancellationToken ct)
    {
        var c = await svc.RequestClaimAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Claim), new { id = c["id"] }, c);
    }

    [HttpPost("claims/{id:long}/approve")] public async Task<IActionResult> Approve(long id, CancellationToken ct) => Ok(await svc.ApproveClaimAsync(me.User, id, ct));
    [HttpPost("claims/{id:long}/reject")] public async Task<IActionResult> Reject(long id, RejectBody body, CancellationToken ct) { await svc.RejectClaimAsync(me.User, id, body, ct); return NoContent(); }
}
