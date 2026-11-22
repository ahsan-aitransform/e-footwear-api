using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Core.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>
/// A person's own preferences (language, theme, default store). Every company user may read and change their OWN preferences, so
/// there is no screen permission; the data is only reachable for the signed-in user (row-level security allows no other row).
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.UserPreferences.Root)]
public sealed class UserPreferencesController(UserPreferencesService userPreferencesService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDocumentResponse<UserPreferencesSettings>>> Get(CancellationToken cancellationToken) =>
        await userPreferencesService.GetAsync(cancellationToken);

    [HttpPut]
    public async Task<ActionResult<SettingsDocumentResponse<UserPreferencesSettings>>> Save(
        SaveSettingsRequest<UserPreferencesSettings> request, CancellationToken cancellationToken) =>
        await userPreferencesService.SaveAsync(request, cancellationToken);
}
