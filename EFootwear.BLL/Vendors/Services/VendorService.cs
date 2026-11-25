using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Contacts.Services;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.Vendors.Abstractions;
using EFootwear.BLL.Vendors.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.Vendors;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Vendors.Services;

/// <summary>
/// Use cases of New Vendor, View Vendors and Search Vendor. Rules: the name and the phone number are required; the email, the birth date and the
/// city are optional; the code is generated; a vendor that has accounting history cannot be deleted (it can be made inactive). The balance is
/// never edited here: it comes from the journal. An opening balance is posted through the accounting opening-balance mechanism and needs the
/// OPENING_BALANCE permission. The credit limit is whole rupees; the database refuses a purchase bill that would take the open bills above it.
/// </summary>
public sealed class VendorService(
    IVendorRepository vendorRepository,
    IContactCodeGenerator codeGenerator,
    ContactFieldValidator fieldValidator,
    ContactOpeningBalanceApplier openingBalanceApplier,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<VendorService> logger)
{
    public const int LookupMaximumItemCount = 50;
    private const string VendorsTableName = "contacts.vendors";

    // The balance table points at every vendor that ever had a balance; it must not block a deletion on its own.
    private static readonly HashSet<string> TablesThatDoNotBlockDeletion = ["contacts.vendor_balances"];

    public Task<PagedResult<VendorListItemResponse>> SearchAsync(VendorSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StatusCode is not null) ContactFieldValidator.EnsureStatusIsValid(filter.StatusCode);
        return vendorRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<IReadOnlyList<VendorLookupItemResponse>> LookupAsync(string? searchText, CancellationToken cancellationToken) =>
        vendorRepository.LookupAsync(string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(), LookupMaximumItemCount, cancellationToken);

    public async Task<VendorDetailResponse> GetAsync(Guid vendorId, CancellationToken cancellationToken) =>
        await vendorRepository.FindDetailAsync(vendorId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The vendor was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreateVendorRequest request, CancellationToken cancellationToken)
    {
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        var code = await codeGenerator.GenerateNextCodeAsync(cancellationToken);
        var vendorId = await vendorRepository.AddAsync(record, code, currentUser.UserId, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Vendor, vendorId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Vendor {VendorId} ({Code}) created in company {CompanyId} by user {UserId}", vendorId, code, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(vendorId);
    }

    public async Task UpdateAsync(Guid vendorId, UpdateVendorRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(vendorId, cancellationToken);
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        await vendorRepository.UpdateAsync(vendorId, record, request.RowVersion, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Vendor, vendorId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(vendorId, cancellationToken);
        var usages = (await rowUsageRepository.FindUsagesAsync(VendorsTableName, vendorId, cancellationToken))
            .Where(usage => !TablesThatDoNotBlockDeletion.Contains(usage.TableName))
            .ToList();
        RowUsageMessage.ThrowWhenUsed($"Vendor {existing.Code} {existing.Name}", usages);

        await vendorRepository.SoftDeleteAsync(vendorId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Vendor {VendorId} deleted in company {CompanyId} by user {UserId}", vendorId, currentUser.CompanyId, currentUser.UserId);
    }

    private async Task<VendorWriteRecord> BuildValidatedRecordAsync(IVendorEditableFields fields, CancellationToken cancellationToken)
    {
        var name = fieldValidator.RequireName(fields.Name, "Vendor");
        var phone = fieldValidator.RequirePhone(fields.Phone, "Vendor");
        ContactFieldValidator.EnsureStatusIsValid(fields.StatusCode);
        VendorRules.EnsureCreditLimitIsValid(fields.CreditLimit);
        var email = fieldValidator.ValidateEmail(fields.Email);
        fieldValidator.EnsureBirthDateIsValid(fields.BirthDate);
        await fieldValidator.EnsureCityIsUsableAsync(fields.CityId, cancellationToken);
        return new VendorWriteRecord(name, phone, email, fields.BirthDate, fields.CityId, fields.CreditLimit, fields.StatusCode);
    }
}
