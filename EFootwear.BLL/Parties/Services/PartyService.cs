using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Parties;
using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Parties.Services;

/// <summary>
/// Use cases of New Party, Manage Party and Search Party (docx 3.1 to 3.3, Part 8). One party record can be customer, vendor and
/// staff at the same time. Rules: the name is required; at least one party type; the CNIC has the shape 12345-1234567-1 and is
/// unique; a staff party needs the store where the person works; the code is generated; a party that has accounting history cannot
/// be deleted (it can be made inactive). The balance is never edited here: it comes from the journal. An opening balance is
/// posted through the accounting opening-balance mechanism and needs the OPENING_BALANCE permission.
/// </summary>
public sealed class PartyService(
    IPartyRepository partyRepository,
    IMasterListRepository masterListRepository,
    IStoreRepository storeRepository,
    IOpeningBalanceRepository openingBalanceRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PartyService> logger)
{
    public const int LookupMaximumItemCount = 50;
    private const string PartiesTableName = "party.parties";

    // These tables point at every staff party or every party that ever had a balance; they must not block a deletion on their own.
    private static readonly HashSet<string> TablesThatDoNotBlockDeletion =
        ["party.staff_profiles", "party.staff_store_history", "party.party_balances"];

    public Task<PagedResult<PartyListItemResponse>> SearchAsync(PartySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (!PartyTypeFilterCodes.IsValid(filter.PartyTypeCode))
            throw new DomainException(ErrorKind.Validation, "The party type must be ALL, CUSTOMER, VENDOR or STAFF.");
        if (filter.StatusCode is not null && !PartyStatusCodes.IsValid(filter.StatusCode))
            throw new DomainException(ErrorKind.Validation, "The status must be ACTIVE or INACTIVE.");
        return partyRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<IReadOnlyList<PartyLookupItemResponse>> LookupAsync(string? partyTypeCode, string? searchText, CancellationToken cancellationToken)
    {
        var typeCode = string.IsNullOrWhiteSpace(partyTypeCode) ? PartyTypeFilterCodes.All : partyTypeCode;
        if (!PartyTypeFilterCodes.IsValid(typeCode))
            throw new DomainException(ErrorKind.Validation, "The party type must be ALL, CUSTOMER, VENDOR or STAFF.");
        return partyRepository.LookupAsync(typeCode, string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(), LookupMaximumItemCount, cancellationToken);
    }

    public async Task<PartyDetailResponse> GetAsync(Guid partyId, CancellationToken cancellationToken) =>
        await partyRepository.FindDetailAsync(partyId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The party was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreatePartyRequest request, CancellationToken cancellationToken)
    {
        var record = await BuildValidatedRecordAsync(request, null, cancellationToken);
        var code = await partyRepository.GenerateNextCodeAsync(cancellationToken);
        var partyId = await partyRepository.AddAsync(record, code, currentUser.UserId, cancellationToken);
        await ApplyOpeningBalanceAsync(partyId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Party {PartyId} ({Code}) created in company {CompanyId} by user {UserId}", partyId, code, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(partyId);
    }

    public async Task UpdateAsync(Guid partyId, UpdatePartyRequest request, CancellationToken cancellationToken)
    {
        _ = await GetAsync(partyId, cancellationToken);
        var record = await BuildValidatedRecordAsync(request, partyId, cancellationToken);
        await partyRepository.UpdateAsync(partyId, record, request.RowVersion, cancellationToken);
        await ApplyOpeningBalanceAsync(partyId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var party = await GetAsync(partyId, cancellationToken);
        var usages = (await rowUsageRepository.FindUsagesAsync(PartiesTableName, partyId, cancellationToken))
            .Where(usage => !TablesThatDoNotBlockDeletion.Contains(usage.TableName))
            .ToList();
        RowUsageMessage.ThrowWhenUsed($"Party {party.Code} {party.Name}", usages);

        await partyRepository.SoftDeleteAsync(partyId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Party {PartyId} deleted in company {CompanyId} by user {UserId}", partyId, currentUser.CompanyId, currentUser.UserId);
    }

    // ------------------------------------------------------------- validation

    private async Task<PartyWriteRecord> BuildValidatedRecordAsync(IPartyEditableFields fields, Guid? existingPartyId, CancellationToken cancellationToken)
    {
        var name = MasterListRules.NormalizeText(fields.Name);
        if (name.Length == 0) throw new DomainException(ErrorKind.Validation, "The party name is required.");
        PartyRules.EnsureLength(name, PartyRules.NameMaximumLength, "The party name");
        PartyRules.EnsureAtLeastOneRole(fields.IsCustomer, fields.IsVendor, fields.IsStaff);
        if (!PartyStatusCodes.IsValid(fields.StatusCode))
            throw new DomainException(ErrorKind.Validation, "The status must be ACTIVE or INACTIVE.");
        if (fields.CreditLimit is < 0)
            throw new DomainException(ErrorKind.Validation, "The credit limit cannot be negative.");

        var nameUrdu = Optional(fields.NameUrdu, PartyRules.NameMaximumLength, "The Urdu name");
        var fatherName = Optional(fields.FatherName, PartyRules.NameMaximumLength, "The father name");
        var fatherNameUrdu = Optional(fields.FatherNameUrdu, PartyRules.NameMaximumLength, "The Urdu father name");
        var address = Optional(fields.Address, PartyRules.AddressMaximumLength, "The address");
        var addressUrdu = Optional(fields.AddressUrdu, PartyRules.AddressMaximumLength, "The Urdu address");
        var notes = Optional(fields.Notes, PartyRules.NotesMaximumLength, "The notes");

        var cnic = await ValidateCnicAsync(fields.Cnic, existingPartyId, cancellationToken);
        var primaryPhone = ValidatePhone(fields.PrimaryPhone, "The contact number");
        var secondaryPhone = ValidatePhone(fields.SecondaryPhone, "The second contact number");
        var email = ValidateEmail(fields.Email);
        await EnsureCityIsUsableAsync(fields.CityId, cancellationToken);

        string? designation = null;
        Guid? staffStoreId = null;
        var isManager = false;
        if (fields.IsStaff)
        {
            designation = Optional(fields.Designation, PartyRules.DesignationMaximumLength, "The designation");
            isManager = fields.IsManager;
            staffStoreId = await RequireStaffStoreAsync(fields.StaffStoreId, cancellationToken);
        }

        return new PartyWriteRecord(name, nameUrdu, fatherName, fatherNameUrdu, cnic, fields.CityId, primaryPhone, secondaryPhone, email,
                                    address, addressUrdu, notes, fields.IsCustomer, fields.IsVendor, fields.IsStaff, fields.CreditLimit,
                                    fields.StatusCode, designation, isManager, staffStoreId);
    }

    private static string? Optional(string? text, int maximumLength, string what)
    {
        var normalized = MasterListRules.NormalizeOptionalText(text);
        PartyRules.EnsureLength(normalized, maximumLength, what);
        return normalized;
    }

    private async Task<string?> ValidateCnicAsync(string? cnic, Guid? existingPartyId, CancellationToken cancellationToken)
    {
        var trimmed = MasterListRules.NormalizeOptionalText(cnic);
        if (trimmed is null) return null;
        if (!PartyRules.IsValidCnic(trimmed))
            throw new DomainException(ErrorKind.Validation, "The CNIC must look like 12345-1234567-1.");
        if (await partyRepository.CnicExistsAsync(trimmed, existingPartyId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Another party already has the CNIC {trimmed}.");
        return trimmed;
    }

    private static string? ValidatePhone(string? phone, string what)
    {
        var trimmed = MasterListRules.NormalizeOptionalText(phone);
        if (trimmed is null) return null;
        if (trimmed.Length > PartyRules.PhoneMaximumLength || !PartyRules.IsValidPhone(trimmed))
            throw new DomainException(ErrorKind.Validation, $"{what} is not a valid phone number.");
        return trimmed;
    }

    private static string? ValidateEmail(string? email)
    {
        var trimmed = MasterListRules.NormalizeOptionalText(email);
        if (trimmed is null) return null;
        if (!PartyRules.IsValidEmail(trimmed))
            throw new DomainException(ErrorKind.Validation, "The email address is not valid.");
        return trimmed;
    }

    private async Task EnsureCityIsUsableAsync(Guid? cityId, CancellationToken cancellationToken)
    {
        if (cityId is null) return;
        var city = await masterListRepository.FindAsync(MasterListDefinitions.City, cityId.Value, cancellationToken);
        if (city is null || !city.IsActive)
            throw new DomainException(ErrorKind.Validation, "Choose an active city / area.");
    }

    private async Task<Guid> RequireStaffStoreAsync(Guid? staffStoreId, CancellationToken cancellationToken)
    {
        if (staffStoreId is null)
            throw new DomainException(ErrorKind.Validation, "A staff member needs the store where the person works.");
        if (!currentUser.CanAccessStore(staffStoreId.Value))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(currentUser.CompanyId, [staffStoreId.Value], cancellationToken);
        if (validStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "Choose an active store of this company.");
        return staffStoreId.Value;
    }

    private async Task ApplyOpeningBalanceAsync(Guid partyId, PartyOpeningBalanceRequest? openingBalance, CancellationToken cancellationToken)
    {
        if (openingBalance is null) return;
        if (!OpeningBalanceSideCodes.IsValid(openingBalance.SideCode))
            throw new DomainException(ErrorKind.Validation, "The opening balance side must be DEBIT or CREDIT.");
        if (openingBalance.Amount < 0)
            throw new DomainException(ErrorKind.Validation, "The opening balance cannot be negative.");
        if (!currentUser.HasPermission(PermissionCodes.RestrictedOperations.Accounting.OpeningBalance))
            throw new DomainException(ErrorKind.Forbidden, "You do not have the permission to enter opening balances.", ErrorCodes.PermissionDenied);

        var isDebit = openingBalance.SideCode == OpeningBalanceSideCodes.Debit;
        await openingBalanceRepository.PostAsync(null, partyId, AccountingScope.HeadOfficeStoreId,
                                                 isDebit ? openingBalance.Amount : 0, isDebit ? 0 : openingBalance.Amount,
                                                 openingBalance.AsOfDate, cancellationToken);
    }
}
