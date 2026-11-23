using EFootwear.BLL.Common;
using EFootwear.BLL.HumanResources.Contracts;

namespace EFootwear.BLL.HumanResources.Abstractions;

/// <summary>Staff profiles, salary terms and the store history of staff (party.staff_profiles, hr.salary_terms, party.staff_store_history). Implemented in the DAL.</summary>
public interface IStaffRepository
{
    Task<PagedResult<StaffListItemResponse>> SearchStaffAsync(StaffSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);

    Task<StaffDetailResponse?> FindStaffDetailAsync(Guid partyId, CancellationToken cancellationToken);

    /// <summary>Saves the new salary terms when the row version still matches; false when the row changed in the meantime or does not exist.</summary>
    Task<bool> UpdateSalaryTermsAsync(Guid partyId, long baseSalary, string payFrequencyCode, long expectedRowVersion, CancellationToken cancellationToken);

    Task<PagedResult<StaffTransferListItemResponse>> SearchTransfersAsync(StaffTransferSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
}
