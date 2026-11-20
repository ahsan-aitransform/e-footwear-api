namespace EFootwear.BLL.Abstractions;

/// <summary>
/// One database transaction per use case. Nothing is saved until <see cref="CommitAsync"/>.
/// If a handler throws, the transaction is rolled back when the request scope ends.
/// </summary>
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct = default);
}
