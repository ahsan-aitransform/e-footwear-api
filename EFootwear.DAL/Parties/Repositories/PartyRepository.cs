using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Parties.Repositories;

/// <summary>
/// Data access for parties, their staff profile and staff store history. Every query is limited to the caller's company (row-level
/// security repeats it) and hides soft-deleted parties. The balance comes from party.party_balances, which the database keeps in
/// step with the journal: nothing here writes a balance.
/// </summary>
public sealed class PartyRepository(DatabaseSession databaseSession, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider) : IPartyRepository
{
    private static readonly SortableFields<PartyRow> SortablePartyFields = new SortableFields<PartyRow>()
        .Allow("name", row => row.Party.Name)
        .Allow("code", row => row.Party.Code)
        .Allow("city", row => row.CityName)
        .Allow("designation", row => row.Designation)
        .Allow("balance", row => row.BalanceAmount);

    public async Task<PagedResult<PartyListItemResponse>> SearchAsync(PartySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        rows = filter.PartyTypeCode switch
        {
            PartyTypeFilterCodes.Customer => rows.Where(row => row.Party.IsCustomer),
            PartyTypeFilterCodes.Vendor => rows.Where(row => row.Party.IsVendor),
            PartyTypeFilterCodes.Staff => rows.Where(row => row.Party.IsStaff),
            _ => rows,
        };
        if (filter.CityId is { } cityId) rows = rows.Where(row => row.Party.CityId == cityId);
        if (filter.StatusCode is { } statusCode) rows = rows.Where(row => row.Party.Status == statusCode);
        if (pageRequest.SearchText is { } searchText) rows = rows.Where(MatchesSearchText(searchText));

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortablePartyFields, row => row.Party.Id).ToListAsync(cancellationToken);
        return new PagedResult<PartyListItemResponse>(page.Select(ToListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<PartyLookupItemResponse>> LookupAsync(string partyTypeCode, string? searchText, int maximumItemCount, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var parties = databaseContext.Parties.AsNoTracking()
            .Where(party => party.CompanyId == companyId && !party.IsDeleted && party.Status == PartyStatusCodes.Active);
        parties = partyTypeCode switch
        {
            PartyTypeFilterCodes.Customer => parties.Where(party => party.IsCustomer),
            PartyTypeFilterCodes.Vendor => parties.Where(party => party.IsVendor),
            PartyTypeFilterCodes.Staff => parties.Where(party => party.IsStaff),
            _ => parties,
        };
        if (searchText is not null)
        {
            var pattern = LikePattern.Contains(searchText);
            parties = parties.Where(party => EF.Functions.ILike(party.Name, pattern, LikePattern.EscapeCharacter)
                                             || EF.Functions.ILike(party.Code, pattern, LikePattern.EscapeCharacter)
                                             || (party.PrimaryPhone != null && EF.Functions.ILike(party.PrimaryPhone, pattern, LikePattern.EscapeCharacter)));
        }

        return await parties.OrderBy(party => party.Name).ThenBy(party => party.Id).Take(maximumItemCount)
            .Select(party => new PartyLookupItemResponse(party.Id, party.Code, party.Name, party.NameUrdu, party.PrimaryPhone,
                                                         party.IsCustomer, party.IsVendor, party.IsStaff))
            .ToListAsync(cancellationToken);
    }

    public async Task<PartyDetailResponse?> FindDetailAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Party.Id == partyId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var staffProfile = await databaseContext.StaffProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.CompanyId == companyId && profile.PartyId == partyId, cancellationToken);
        string? staffStoreName = null;
        if (staffProfile is not null)
            staffStoreName = await databaseContext.Stores.AsNoTracking()
                .Where(store => store.CompanyId == companyId && store.Id == staffProfile.CurrentStoreId)
                .Select(store => store.Name).SingleOrDefaultAsync(cancellationToken);

        var openingBalance = await databaseContext.Database.SqlQuery<OpeningBalanceRow>($"""
            select entry_date as "AsOfDate", debit as "DebitAmount", credit as "CreditAmount"
              from acct.v_live_opening_balances
             where company_id = {companyId} and party_id = {partyId}
            """).ToListAsync(cancellationToken);
        var opening = openingBalance.FirstOrDefault();

        var (fatherNameUrdu, addressUrdu, notes) = PartyAttributesJson.Read(row.Party.AttributesJson);
        var party = row.Party;
        return new PartyDetailResponse(
            party.Id, party.Code, party.Name, party.NameUrdu, party.FatherName, fatherNameUrdu, party.Cnic, party.CityId, row.CityName,
            party.PrimaryPhone, party.SecondaryPhone, party.Email, party.Address, addressUrdu, notes,
            party.IsCustomer, party.IsVendor, party.IsStaff, party.CreditLimit, party.Status,
            staffProfile?.Designation, staffProfile?.IsManager ?? false, staffProfile?.CurrentStoreId, staffStoreName,
            party.CreatedAt, row.BalanceAmount,
            opening is null ? null : new PartyOpeningBalanceResponse(
                opening.DebitAmount > 0 ? OpeningBalanceSideCodes.Debit : OpeningBalanceSideCodes.Credit,
                opening.DebitAmount > 0 ? opening.DebitAmount : opening.CreditAmount, opening.AsOfDate),
            party.RowVersion);
    }

    public async Task<bool> CnicExistsAsync(string cnic, Guid? exceptPartyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Parties.AsNoTracking().AnyAsync(party =>
            party.CompanyId == companyId && !party.IsDeleted && party.Cnic == cnic && (exceptPartyId == null || party.Id != exceptPartyId), cancellationToken);
    }

    public async Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<string>($"select party.next_party_code({companyId}) as \"Value\"").SingleAsync(cancellationToken);
    }

    public async Task<Guid> AddAsync(PartyWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var party = new Party
        {
            CompanyId = currentUser.CompanyId,
            Code = code,
            CreatedBy = createdBy,
            AttributesJson = PartyAttributesJson.Merge("{}", record.FatherNameUrdu, record.AddressUrdu, record.Notes),
        };
        ApplyFields(party, record);
        databaseContext.Parties.Add(party);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the database generates the id; later steps need it

        if (record.IsStaff) await AddStaffProfileAsync(databaseContext, party.Id, record, createdBy, cancellationToken);
        return party.Id;
    }

    public async Task UpdateAsync(Guid partyId, PartyWriteRecord record, long rowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var party = await databaseContext.Parties.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == partyId && !candidate.IsDeleted, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The party was not found.");

        ApplyFields(party, record);
        party.AttributesJson = PartyAttributesJson.Merge(party.AttributesJson, record.FatherNameUrdu, record.AddressUrdu, record.Notes);
        databaseContext.Entry(party).Property(entity => entity.RowVersion).OriginalValue = rowVersion;

        if (!record.IsStaff) return;
        var staffProfile = await databaseContext.StaffProfiles.SingleOrDefaultAsync(profile => profile.CompanyId == companyId && profile.PartyId == partyId, cancellationToken);
        if (staffProfile is null)
        {
            await databaseContext.SaveChangesAsync(cancellationToken);
            await AddStaffProfileAsync(databaseContext, partyId, record, currentUser.UserId, cancellationToken);
            return;
        }
        // The store of an existing staff member only changes through a transfer, which leaves a history row.
        staffProfile.Designation = record.Designation;
        staffProfile.IsManager = record.IsManager;
    }

    public async Task SoftDeleteAsync(Guid partyId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var party = await databaseContext.Parties.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == partyId && !candidate.IsDeleted, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The party was not found.");
        party.IsDeleted = true;
        party.DeletedAt = dateTimeProvider.UtcNow;
        party.DeletedBy = deletedBy;
    }

    public async Task<Guid?> FindStaffStoreIdAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.StaffProfiles.AsNoTracking()
            .Where(profile => profile.CompanyId == companyId && profile.PartyId == partyId)
            .Select(profile => (Guid?)profile.CurrentStoreId).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task TransferStaffAsync(Guid partyId, Guid fromStoreId, Guid toStoreId, DateOnly effectiveOn, Guid changedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var staffProfile = await databaseContext.StaffProfiles.SingleAsync(profile => profile.CompanyId == companyId && profile.PartyId == partyId, cancellationToken);
        staffProfile.CurrentStoreId = toStoreId;
        databaseContext.StaffStoreHistories.Add(new StaffStoreHistory
        {
            CompanyId = companyId,
            PartyId = partyId,
            FromStoreId = fromStoreId,
            ToStoreId = toStoreId,
            EffectiveOn = effectiveOn,
            ChangedBy = changedBy,
        });
    }

    public async Task<IReadOnlyList<StaffStoreHistoryResponse>> ListStaffStoreHistoryAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await (from history in databaseContext.StaffStoreHistories.AsNoTracking()
                          where history.CompanyId == companyId && history.PartyId == partyId
                          join toStore in databaseContext.Stores.AsNoTracking() on new { history.CompanyId, Id = history.ToStoreId } equals new { toStore.CompanyId, toStore.Id }
                          join fromStore in databaseContext.Stores.AsNoTracking() on new { history.CompanyId, Id = history.FromStoreId } equals new { fromStore.CompanyId, Id = (Guid?)fromStore.Id } into fromGroup
                          from fromStore in fromGroup.DefaultIfEmpty()
                          orderby history.EffectiveOn descending, history.ChangedAt descending
                          select new StaffHistoryRow { History = history, ToStoreName = toStore.Name, FromStoreName = fromStore == null ? null : fromStore.Name })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new StaffStoreHistoryResponse(row.History.Id, row.History.FromStoreId, row.FromStoreName, row.History.ToStoreId,
                                                                row.ToStoreName, row.History.EffectiveOn, row.History.ChangedBy, row.History.ChangedAt)).ToList();
    }

    // ---------------------------------------------------------------- helpers

    private async Task AddStaffProfileAsync(ApplicationDatabaseContext databaseContext, Guid partyId, PartyWriteRecord record, Guid createdBy, CancellationToken cancellationToken)
    {
        var storeId = record.StaffStoreId ?? throw new DomainException(ErrorKind.Validation, "A staff member needs the store where the person works.");
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.UtcDateTime);
        databaseContext.StaffProfiles.Add(new StaffProfile
        {
            CompanyId = currentUser.CompanyId,
            PartyId = partyId,
            Designation = record.Designation,
            IsManager = record.IsManager,
            CurrentStoreId = storeId,
            JoinedOn = today,
            CreatedBy = createdBy,
        });
        databaseContext.StaffStoreHistories.Add(new StaffStoreHistory
        {
            CompanyId = currentUser.CompanyId,
            PartyId = partyId,
            FromStoreId = null,
            ToStoreId = storeId,
            EffectiveOn = today,
            ChangedBy = createdBy,
        });
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyFields(Party party, PartyWriteRecord record)
    {
        party.Name = record.Name;
        party.NameUrdu = record.NameUrdu;
        party.FatherName = record.FatherName;
        party.Cnic = record.Cnic;
        party.CityId = record.CityId;
        party.PrimaryPhone = record.PrimaryPhone;
        party.SecondaryPhone = record.SecondaryPhone;
        party.Email = record.Email;
        party.Address = record.Address;
        party.IsCustomer = record.IsCustomer;
        party.IsVendor = record.IsVendor;
        party.IsStaff = record.IsStaff;
        party.CreditLimit = record.CreditLimit;
        party.Status = record.StatusCode;
    }

    private async Task<IQueryable<PartyRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var cities = databaseContext.Set<MasterListItem>(MasterListDefinitions.City.EntityName).AsNoTracking();
        return from party in databaseContext.Parties.AsNoTracking()
               where party.CompanyId == companyId && !party.IsDeleted
               join city in cities on new { party.CompanyId, Id = party.CityId } equals new { city.CompanyId, Id = (Guid?)city.Id } into cityGroup
               from city in cityGroup.DefaultIfEmpty()
               join staff in databaseContext.StaffProfiles.AsNoTracking() on new { party.CompanyId, PartyId = party.Id } equals new { staff.CompanyId, staff.PartyId } into staffGroup
               from staff in staffGroup.DefaultIfEmpty()
               join balance in databaseContext.PartyBalances.AsNoTracking() on new { party.CompanyId, PartyId = party.Id } equals new { balance.CompanyId, balance.PartyId } into balanceGroup
               from balance in balanceGroup.DefaultIfEmpty()
               select new PartyRow
               {
                   Party = party,
                   CityName = city == null ? null : city.Name,
                   Designation = staff == null ? null : staff.Designation,
                   BalanceAmount = balance == null ? 0 : balance.Balance,
               };
    }

    private static System.Linq.Expressions.Expression<Func<PartyRow, bool>> MatchesSearchText(string searchText)
    {
        var pattern = LikePattern.Contains(searchText);
        return row => EF.Functions.ILike(row.Party.Name, pattern, LikePattern.EscapeCharacter)
                      || EF.Functions.ILike(row.Party.Code, pattern, LikePattern.EscapeCharacter)
                      || (row.Party.NameUrdu != null && EF.Functions.ILike(row.Party.NameUrdu, pattern, LikePattern.EscapeCharacter))
                      || (row.Party.FatherName != null && EF.Functions.ILike(row.Party.FatherName, pattern, LikePattern.EscapeCharacter))
                      || (row.Party.Cnic != null && EF.Functions.ILike(row.Party.Cnic, pattern, LikePattern.EscapeCharacter))
                      || (row.Party.Address != null && EF.Functions.ILike(row.Party.Address, pattern, LikePattern.EscapeCharacter))
                      || (row.Party.PrimaryPhone != null && EF.Functions.ILike(row.Party.PrimaryPhone, pattern, LikePattern.EscapeCharacter))
                      || (row.Party.SecondaryPhone != null && EF.Functions.ILike(row.Party.SecondaryPhone, pattern, LikePattern.EscapeCharacter))
                      || (row.CityName != null && EF.Functions.ILike(row.CityName, pattern, LikePattern.EscapeCharacter))
                      || (row.Designation != null && EF.Functions.ILike(row.Designation, pattern, LikePattern.EscapeCharacter));
    }

    private static PartyListItemResponse ToListItem(PartyRow row) => new(
        row.Party.Id, row.Party.Code, row.Party.Name, row.Party.NameUrdu, row.Party.FatherName, row.Party.Cnic, row.Party.Address,
        row.Party.CityId, row.CityName, row.Party.PrimaryPhone, row.Party.SecondaryPhone, row.Designation,
        row.Party.IsCustomer, row.Party.IsVendor, row.Party.IsStaff, row.Party.Status, row.BalanceAmount, row.Party.RowVersion);

    private sealed class PartyRow
    {
        public Party Party { get; set; } = null!;
        public string? CityName { get; set; }
        public string? Designation { get; set; }
        public long BalanceAmount { get; set; }
    }

    private sealed class StaffHistoryRow
    {
        public StaffStoreHistory History { get; set; } = null!;
        public string ToStoreName { get; set; } = "";
        public string? FromStoreName { get; set; }
    }

    private sealed class OpeningBalanceRow
    {
        public DateOnly AsOfDate { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
    }
}
