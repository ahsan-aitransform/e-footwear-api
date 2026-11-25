using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Contacts.Services;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Staff.Abstractions;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Staff.Services;

/// <summary>
/// Use cases of New Staff, View Staff and Search Staff. Rules: the name and the phone number are required; a staff member works in a store the
/// caller may use; the code is generated; a staff member that has accounting or payroll history cannot be deleted (it can be made inactive). The
/// balance is never edited here: it comes from the journal. An opening balance is posted through the accounting opening-balance mechanism and
/// needs the OPENING_BALANCE permission.
/// </summary>
public sealed class StaffMemberService(
    IStaffMemberRepository staffRepository,
    IContactCodeGenerator codeGenerator,
    ContactFieldValidator fieldValidator,
    ContactOpeningBalanceApplier openingBalanceApplier,
    IStoreRepository storeRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StaffMemberService> logger)
{
    public const int LookupMaximumItemCount = 50;
    private const string StaffTableName = "contacts.staff";

    // These tables point at every staff member that ever had a balance, a store or a first salary term (written by a trigger); they must not block a deletion on their own.
    private static readonly HashSet<string> TablesThatDoNotBlockDeletion = ["contacts.staff_balances", "contacts.staff_store_history", "hr.salary_terms"];

    public Task<PagedResult<StaffMemberListItemResponse>> SearchAsync(StaffMemberSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StatusCode is not null) ContactFieldValidator.EnsureStatusIsValid(filter.StatusCode);
        return staffRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<IReadOnlyList<StaffMemberLookupItemResponse>> LookupAsync(string? searchText, CancellationToken cancellationToken) =>
        staffRepository.LookupAsync(string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(), LookupMaximumItemCount, cancellationToken);

    public async Task<StaffMemberDetailResponse> GetAsync(Guid staffId, CancellationToken cancellationToken) =>
        await staffRepository.FindDetailAsync(staffId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreateStaffMemberRequest request, CancellationToken cancellationToken)
    {
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        var storeId = await RequireStoreAsync(request.StoreId, cancellationToken);
        var code = await codeGenerator.GenerateNextCodeAsync(cancellationToken);
        var staffId = await staffRepository.AddAsync(record, storeId, code, currentUser.UserId, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Staff, staffId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Staff member {StaffId} ({Code}) created in company {CompanyId} by user {UserId}", staffId, code, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(staffId);
    }

    public async Task UpdateAsync(Guid staffId, UpdateStaffMemberRequest request, CancellationToken cancellationToken)
    {
        _ = await GetAsync(staffId, cancellationToken);
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        await staffRepository.UpdateAsync(staffId, record, request.RowVersion, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Staff, staffId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(staffId, cancellationToken);
        var usages = (await rowUsageRepository.FindUsagesAsync(StaffTableName, staffId, cancellationToken))
            .Where(usage => !TablesThatDoNotBlockDeletion.Contains(usage.TableName))
            .ToList();
        RowUsageMessage.ThrowWhenUsed($"Staff {existing.Code} {existing.Name}", usages);

        await staffRepository.SoftDeleteAsync(staffId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Staff member {StaffId} deleted in company {CompanyId} by user {UserId}", staffId, currentUser.CompanyId, currentUser.UserId);
    }

    private async Task<StaffMemberWriteRecord> BuildValidatedRecordAsync(IStaffMemberEditableFields fields, CancellationToken cancellationToken)
    {
        var name = fieldValidator.RequireName(fields.Name, "Staff");
        var phone = fieldValidator.RequirePhone(fields.Phone, "Staff");
        ContactFieldValidator.EnsureStatusIsValid(fields.StatusCode);
        var email = fieldValidator.ValidateEmail(fields.Email);
        fieldValidator.EnsureBirthDateIsValid(fields.BirthDate);
        await fieldValidator.EnsureCityIsUsableAsync(fields.CityId, cancellationToken);
        var designation = fieldValidator.OptionalText(fields.Designation, ContactRules.DesignationMaximumLength, "The designation");
        return new StaffMemberWriteRecord(name, phone, email, fields.BirthDate, fields.CityId, designation, fields.IsManager, fields.StatusCode);
    }

    private async Task<Guid> RequireStoreAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        if (storeId is null)
            throw new DomainException(ErrorKind.Validation, "A staff member needs the store where the person works.");
        if (!currentUser.CanAccessStore(storeId.Value))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(currentUser.CompanyId, [storeId.Value], cancellationToken);
        if (validStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "Choose an active store of this company.");
        return storeId.Value;
    }
}
