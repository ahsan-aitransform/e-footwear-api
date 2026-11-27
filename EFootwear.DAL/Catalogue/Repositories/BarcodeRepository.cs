using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using EFootwear.DAL.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Barcode lookups, the two-step replacement and its history. A unique index forbids two sizes with one barcode even for an instant, so the replacement first
/// parks every affected size on a temporary barcode and then sets the final ones in one statement; a swap (A to B and B to A) works the same way as a chain.
/// </summary>
public sealed class BarcodeRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IBarcodeRepository
{
    private const string TemporaryBarcodePrefix = "~";

    private static readonly SortableFields<BarcodeHistoryRow> SortableHistoryFields = new SortableFields<BarcodeHistoryRow>()
        .Allow("changedAt", row => row.ChangedAt)
        .Allow("productName", row => row.ProductName)
        .Allow("oldBarcode", row => row.OldBarcode)
        .Allow("newBarcode", row => row.NewBarcode);

    public async Task<IReadOnlyList<VariantBarcodeOwner>> FindOwnersAsync(IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0) return [];
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var barcodeArray = barcodes.ToArray();
        return await (from variant in databaseContext.ProductVariants.AsNoTracking()
                      join product in databaseContext.Products.AsNoTracking()
                          on new { variant.CompanyId, Id = variant.ProductId } equals new { product.CompanyId, product.Id }
                      where variant.CompanyId == companyId && !variant.IsDeleted && !product.IsDeleted && barcodeArray.Contains(variant.Barcode)
                      select new VariantBarcodeOwner(variant.Id, variant.Barcode, product.Name, variant.SizeCode)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> FindBarcodesInUseAsync(IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0) return [];
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var barcodeArray = barcodes.ToArray();
        var usedByVariants = await databaseContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.CompanyId == companyId && !variant.IsDeleted && barcodeArray.Contains(variant.Barcode))
            .Select(variant => variant.Barcode).ToListAsync(cancellationToken);
        var usedByBundles = await databaseContext.Database.SqlQuery<string>($"""
            select barcode as "Value" from promo.bundles
            where company_id = {companyId} and not is_deleted and barcode = any({barcodeArray})
            """).ToListAsync(cancellationToken);
        return usedByVariants.Concat(usedByBundles).Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<bool> BatchExistsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.BarcodeHistoryEntries.AsNoTracking().AnyAsync(entry => entry.CompanyId == companyId && entry.BatchId == batchId, cancellationToken);
    }

    public async Task ReplaceAsync(Guid batchId, IReadOnlyList<BarcodeHistoryWriteData> replacements, Guid changedBy, CancellationToken cancellationToken)
    {
        if (replacements.Count == 0) return;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variantIds = replacements.Select(replacement => replacement.VariantId).ToArray();
        var oldBarcodes = replacements.Select(replacement => replacement.OldBarcode).ToArray();
        var newBarcodes = replacements.Select(replacement => replacement.NewBarcode).ToArray();

        // Step 1: park the sizes on barcodes nobody else can have (the id makes each one unique).
        await databaseContext.Database.ExecuteSqlAsync($"""
            update catalog.variants set barcode = {TemporaryBarcodePrefix} || id::text
            where company_id = {companyId} and id = any({variantIds}) and not is_deleted
            """, cancellationToken);

        // Step 2: set the final barcodes in one statement.
        var updated = await databaseContext.Database.ExecuteSqlAsync($"""
            update catalog.variants v set barcode = m.new_barcode
            from unnest({variantIds}::uuid[], {newBarcodes}::text[]) as m(variant_id, new_barcode)
            where v.company_id = {companyId} and v.id = m.variant_id
            """, cancellationToken);
        if (updated != replacements.Count)
            throw new InvalidOperationException($"Expected to replace {replacements.Count} barcodes but {updated} sizes were updated.");

        var reasons = replacements.Select(replacement => replacement.Reason ?? "").ToArray();
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into catalog.barcode_history (company_id, id, variant_id, old_barcode, new_barcode, batch_id, reason, changed_by)
            select {companyId}, uuidv7(), m.variant_id, m.old_barcode, m.new_barcode, {batchId}, nullif(m.reason, ''), {changedBy}
            from unnest({variantIds}::uuid[], {oldBarcodes}::text[], {newBarcodes}::text[], {reasons}::text[]) as m(variant_id, old_barcode, new_barcode, reason)
            """, cancellationToken);
    }

    public async Task<PagedResult<BarcodeHistoryEntryResponse>> ListHistoryAsync(string? barcode, Guid? variantId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = from entry in databaseContext.BarcodeHistoryEntries.AsNoTracking()
                   join variant in databaseContext.ProductVariants.AsNoTracking()
                       on new { entry.CompanyId, Id = entry.VariantId } equals new { variant.CompanyId, variant.Id }
                   join product in databaseContext.Products.AsNoTracking()
                       on new { variant.CompanyId, Id = variant.ProductId } equals new { product.CompanyId, product.Id }
                   join user in databaseContext.CompanyUsers.AsNoTracking()
                       on new { entry.CompanyId, Id = entry.ChangedBy } equals new { user.CompanyId, Id = (Guid?)user.Id } into userGroup
                   from user in userGroup.DefaultIfEmpty()
                   where entry.CompanyId == companyId
                   select new BarcodeHistoryRow
                   {
                       Id = entry.Id, VariantId = entry.VariantId, ProductName = product.Name, SizeCode = variant.SizeCode, OldBarcode = entry.OldBarcode,
                       NewBarcode = entry.NewBarcode, BatchId = entry.BatchId, Reason = entry.Reason, ChangedAt = entry.ChangedAt,
                       ChangedByName = user == null ? null : user.FullName,
                   };
        if (variantId is { } selectedVariantId) rows = rows.Where(row => row.VariantId == selectedVariantId);
        if (barcode is not null)
        {
            var wholeBarcode = LikePattern.Exactly(barcode);
            rows = rows.Where(row => EF.Functions.ILike(row.OldBarcode, wholeBarcode, LikePattern.EscapeCharacter)
                                     || EF.Functions.ILike(row.NewBarcode, wholeBarcode, LikePattern.EscapeCharacter));
        }
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = LikePattern.Contains(searchText);
            rows = rows.Where(row => EF.Functions.ILike(row.OldBarcode, pattern, LikePattern.EscapeCharacter)
                                     || EF.Functions.ILike(row.NewBarcode, pattern, LikePattern.EscapeCharacter)
                                     || EF.Functions.ILike(row.ProductName, pattern, LikePattern.EscapeCharacter));
        }

        var total = await rows.LongCountAsync(cancellationToken);
        var ordered = SortableHistoryFields.TryOrder(rows, pageRequest.SortField, pageRequest.SortDirection, out var requestedOrder)
            ? requestedOrder.ThenByDescending(row => row.Id)
            : rows.OrderByDescending(row => row.ChangedAt).ThenByDescending(row => row.Id);
        var page = await ordered.Skip(pageRequest.SkippedItemCount).Take(pageRequest.PageSize).ToListAsync(cancellationToken);
        var items = page.Select(row => new BarcodeHistoryEntryResponse(row.Id, row.VariantId, row.ProductName, row.SizeCode, row.OldBarcode, row.NewBarcode,
            row.BatchId, row.Reason, row.ChangedAt, row.ChangedByName)).ToList();
        return new PagedResult<BarcodeHistoryEntryResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, total);
    }

    public async Task AddHistoryAsync(IReadOnlyList<BarcodeHistoryWriteData> entries, Guid changedBy, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        databaseContext.BarcodeHistoryEntries.AddRange(entries.Select(entry => new BarcodeHistoryEntry
        {
            CompanyId = companyId, Id = Guid.CreateVersion7(), VariantId = entry.VariantId, OldBarcode = entry.OldBarcode, NewBarcode = entry.NewBarcode,
            BatchId = entry.BatchId, Reason = entry.Reason, ChangedBy = changedBy,
        }));
    }

    private sealed class BarcodeHistoryRow
    {
        public Guid Id { get; set; }
        public Guid VariantId { get; set; }
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public string OldBarcode { get; set; } = "";
        public string NewBarcode { get; set; } = "";
        public Guid? BatchId { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
        public string? ChangedByName { get; set; }
    }
}
