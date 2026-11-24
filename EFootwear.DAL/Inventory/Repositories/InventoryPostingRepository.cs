using System.Text.Json;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Inventory.Repositories;

/// <summary>
/// Writes of the Inventory module. Each method calls one database function (migration 0400, or the baseline for dispatch, closing, claim decisions and loss
/// decisions). A refusal of a permission inside a function is turned back into a "forbidden" answer with the readable sentence the function raised.
/// </summary>
public sealed class InventoryPostingRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IInventoryPostingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> PostAdjustmentAsync(Guid storeId, DateOnly adjustmentDate, Guid clientTransactionId, string? description,
        IReadOnlyList<StockMovementLine> lines, string kindCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var linesJson = JsonSerializer.Serialize(lines.Select(line => new
        {
            variantId = line.VariantId,
            quantity = line.Quantity,
            unitCost = line.UnitCostAmount,
            reason = line.ReasonText,
            expectedQuantity = line.ExpectedQuantity,
            countedQuantity = line.CountedQuantity,
        }), JsonOptions);
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.post_stock_adjustment({companyId}, {storeId}, {adjustmentDate}, {clientTransactionId}, {description},
                                             cast({linesJson} as jsonb), {kindCode}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<Guid> VoidAdjustmentAsync(Guid documentId, string reason, Guid clientTransactionId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.void_stock_adjustment({companyId}, {documentId}, {reason}, {clientTransactionId}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<Guid> CreateAuditAsync(Guid storeId, DateOnly auditDate, Guid clientTransactionId, string? note, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.create_stock_audit({companyId}, {storeId}, {auditDate}, {clientTransactionId}, {note}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<AuditScanOutcome> RecordAuditScanAsync(Guid auditId, string barcode, int quantity, Guid deviceScanId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var json = await RunAsync(async () => await databaseContext.Database.SqlQuery<string>($"""
            select cast(inv.record_audit_scan({companyId}, {auditId}, {barcode}, {quantity}, {deviceScanId}) as text) as "Value"
            """).SingleAsync(cancellationToken));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new AuditScanOutcome(root.GetProperty("variantId").GetGuid(), root.GetProperty("countedQuantity").GetDecimal(), root.GetProperty("replayed").GetBoolean());
    }

    public async Task<Guid> PostAuditAsync(Guid auditId, bool includeUnscannedItems, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.post_stock_audit({companyId}, {auditId}, {includeUnscannedItems}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task CancelAuditAsync(Guid auditId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.cancel_stock_audit({companyId}, {auditId})", cancellationToken));
    }

    public async Task<Guid> RequestClaimAsync(Guid storeId, DateOnly claimDate, Guid clientTransactionId, Guid variantId, decimal quantity, string reasonCode, string? note,
        CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.request_claim({companyId}, {storeId}, {claimDate}, {clientTransactionId}, {variantId}, {quantity}, {reasonCode}, {note}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task ApproveClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.approve_claim({companyId}, {claimId}, {userId})", cancellationToken));
    }

    public async Task RejectClaimAsync(Guid claimId, string reason, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.reject_claim({companyId}, {claimId}, {userId}, {reason})", cancellationToken));
    }

    public async Task<Guid> CreateTransferAsync(Guid fromStoreId, Guid toStoreId, Guid clientTransactionId, string? note, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select inv.create_transfer({companyId}, {fromStoreId}, {toStoreId}, {clientTransactionId}, {note}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<TransferScanOutcome> RecordTransferScanAsync(Guid transferId, Guid storeId, string phaseCode, string barcode, int quantity, Guid scanBatchId,
        CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var json = await RunAsync(async () => await databaseContext.Database.SqlQuery<string>($"""
            select cast(inv.record_transfer_scan({companyId}, {transferId}, {storeId}, {phaseCode}, {barcode}, {quantity}, {scanBatchId}) as text) as "Value"
            """).SingleAsync(cancellationToken));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new TransferScanOutcome(root.GetProperty("variantId").GetGuid(), root.GetProperty("quantityScanned").GetDecimal(),
            root.GetProperty("quantityReceived").GetDecimal(), root.GetProperty("replayed").GetBoolean());
    }

    public async Task ShipTransferAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.ship_transfer({companyId}, {transferId}, {userId})", cancellationToken));
    }

    public async Task CloseTransferAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.close_transfer({companyId}, {transferId}, {userId})", cancellationToken));
    }

    public async Task CancelTransferAsync(Guid transferId, string? reason, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync($"select inv.cancel_transfer({companyId}, {transferId}, {reason})", cancellationToken));
    }

    public async Task DecideLossCaseAsync(Guid lossCaseId, string decisionCode, string? bearerCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        await RunAsync(async () => await databaseContext.Database.ExecuteSqlAsync(
            $"select inv.decide_loss_case({companyId}, {lossCaseId}, {decisionCode}, {bearerCode}, {userId})", cancellationToken));
    }

    // The functions raise insufficient_privilege with a readable sentence when the user lacks a permission. The generic mapping would answer a bare
    // "Not allowed", so the sentence is carried over (a row-level security refusal keeps the generic answer).
    private static async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege && !exception.MessageText.Contains("row-level security"))
        {
            throw new DomainException(ErrorKind.Forbidden, exception.MessageText);
        }
    }
}
