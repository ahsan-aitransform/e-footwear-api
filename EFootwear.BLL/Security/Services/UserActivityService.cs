using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Users Activity (docx 1.4, Part 29): who did what, where and when, with before and after values. The feed joins the data-change log
/// and the sign-in events. A user who does not see every store sees only the activity of the people of their own stores.
/// </summary>
public sealed class UserActivityService(IUserActivityRepository activityRepository, RequestContext requestContext)
{
    public const int MaximumRangeDays = 366;

    public async Task<PagedResult<UserActivityEntryResponse>> QueryAsync(UserActivityQuery query, CancellationToken cancellationToken)
    {
        if (query.EntryKind is not null
            && query.EntryKind is not (UserActivityEntryKinds.BusinessChange or UserActivityEntryKinds.SignIn))
            throw new DomainException(ErrorKind.Validation, "The kind must be BUSINESS_CHANGE or SIGN_IN.");
        if (query.From is { } from && query.To is { } to)
        {
            if (from > to) throw new DomainException(ErrorKind.Validation, "The start date must not be after the end date.");
            if (to - from > TimeSpan.FromDays(MaximumRangeDays))
                throw new DomainException(ErrorKind.Validation, $"Choose a range of at most {MaximumRangeDays} days.");
        }

        IReadOnlyList<Guid>? storeScope = requestContext.HasAccessToAllStores ? null : requestContext.AssignedStoreIds.ToList();
        return await activityRepository.QueryAsync(query with { Page = query.Page.Normalized() }, storeScope, cancellationToken);
    }
}
