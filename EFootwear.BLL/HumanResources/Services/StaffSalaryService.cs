using EFootwear.BLL.Abstractions;
using EFootwear.BLL.HumanResources.Abstractions;
using EFootwear.BLL.HumanResources.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.HumanResources;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.HumanResources.Services;

/// <summary>
/// Changes the salary terms of a staff member (docx 3.8 "Basic Pay", Part 16.1). The base salary and pay frequency are saved on the staff profile; the
/// database adds an effective-dated row to the salary history (hr.salary_terms), so earlier salary sheets can still be explained. Salary sheets that are
/// already approved are never changed by a new salary.
/// </summary>
public sealed class StaffSalaryService(
    IStaffRepository staffRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StaffSalaryService> logger)
{
    public async Task UpdateSalaryTermsAsync(Guid staffId, UpdateStaffSalaryTermsRequest request, CancellationToken cancellationToken)
    {
        StaffRules.EnsureSalaryTermsAreValid(request.BaseSalary, request.PayFrequencyCode);

        var detail = await staffRepository.FindStaffDetailAsync(staffId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        if (!currentUser.CanAccessStore(detail.Profile.CurrentStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to the store of this staff member.");

        var wasSaved = await staffRepository.UpdateSalaryTermsAsync(staffId, request.BaseSalary, request.PayFrequencyCode, request.RowVersion, cancellationToken);
        if (!wasSaved)
            throw new DomainException(ErrorKind.Conflict, "The staff member was changed by someone else. Reload and try again.", ErrorCodes.ConcurrentUpdate);

        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Salary terms of staff {StaffId} changed by user {UserId}", staffId, currentUser.UserId);
    }
}
