namespace EFootwear.BLL.Common;

/// <summary>
/// "Where is this record used?" for the delete rule of master data (docx 2.4: blocked if the value is used anywhere, show where).
/// The database answers from its foreign keys (core.find_row_usages), so tables added later are covered without code changes.
/// Implemented in EFootwear.DAL/Common/RowUsageRepository.cs.
/// </summary>
public interface IRowUsageRepository
{
    /// <param name="tableName">Schema-qualified table of the record, for example "master.departments". Must be a table of this system.</param>
    Task<IReadOnlyList<RowUsageResponse>> FindUsagesAsync(string tableName, Guid rowId, CancellationToken cancellationToken);
}
