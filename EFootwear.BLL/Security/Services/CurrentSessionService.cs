using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Builds the answer of GET /auth/me: who is signed in, where they may work and what they may do. The frontend uses it to
/// choose the area (platform or company), to hide menu entries and to guard routes. It is a convenience for the screens;
/// the server still checks every permission on every request.
/// </summary>
public sealed class CurrentSessionService(
    RequestContext requestContext,
    IUserAuthenticationRepository authenticationRepository,
    IStoreRepository storeRepository)
{
    public async Task<CurrentSessionResponse> GetCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (requestContext.IsPlatformAdministrator)
        {
            return new CurrentSessionResponse(
                Audience: SessionAudienceCodes.PlatformAdministrator,
                DisplayName: requestContext.PlatformAdministratorFullName ?? "",
                EmailOrUsername: requestContext.PlatformAdministratorUsername ?? "",
                PlatformAdministratorId: requestContext.PlatformAdministratorId,
                CompanyId: null,
                CompanyName: null,
                UserId: null,
                RoleCode: requestContext.RoleCode,
                HasAccessToAllStores: true,
                AccessibleStoreIds: [],
                MustChangePassword: false,
                GrantedPermissionCodes: []);
        }

        var profile = await authenticationRepository.GetCurrentUserProfileAsync(
            requestContext.CompanyId, requestContext.UserId, cancellationToken);
        var accessibleStores = await storeRepository.ListAccessibleAsync(cancellationToken);

        return new CurrentSessionResponse(
            Audience: SessionAudienceCodes.CompanyUser,
            DisplayName: profile.FullName,
            EmailOrUsername: profile.Email,
            PlatformAdministratorId: null,
            CompanyId: requestContext.CompanyId,
            CompanyName: profile.CompanyName,
            UserId: requestContext.UserId,
            RoleCode: requestContext.RoleCode,
            HasAccessToAllStores: requestContext.HasAccessToAllStores,
            AccessibleStoreIds: accessibleStores.Select(store => store.Id).ToList(),
            MustChangePassword: requestContext.MustChangePassword,
            GrantedPermissionCodes: requestContext.GrantedPermissionCodes.OrderBy(code => code, StringComparer.Ordinal).ToList());
    }
}
