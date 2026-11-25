using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.CashManagement.Abstractions;

/// <summary>Data access for customer promises (cash.promises). Implemented in EFootwear.DAL/CashManagement/Repositories/PromiseRepository.cs.</summary>
public interface IPromiseRepository
{
    Task<PromiseListResponse> SearchAsync(PromiseSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<PromiseResponse?> FindAsync(Guid promiseId, CancellationToken cancellationToken);

    /// <summary>Inserts the promise with the next promise number of its store and saves; returns the new id.</summary>
    Task<Guid> AddAsync(CreatePromiseRequest request, Guid createdBy, CancellationToken cancellationToken);

    Task UpdateAsync(Guid promiseId, DateOnly dueDate, long promisedAmount, string? note, long rowVersion, CancellationToken cancellationToken);

    /// <summary>"Delete" of the docx: the promise is cancelled, never removed, so the history stays.</summary>
    Task CancelAsync(Guid promiseId, long rowVersion, CancellationToken cancellationToken);

    Task<CustomerBalanceResponse?> GetCustomerBalanceAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>True when the customer exists, is not deleted and is active.</summary>
    Task<bool> IsActiveCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}
