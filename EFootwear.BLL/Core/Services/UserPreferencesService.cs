using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Settings;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core.Settings;

namespace EFootwear.BLL.Core.Services;

/// <summary>A person's own preferences (language, theme, default store), kept on the server so they follow the person to another device.</summary>
public sealed class UserPreferencesService(
    ISettingsRepository settingsRepository,
    StoreAccessGuard storeAccessGuard,
    RequestContext requestContext,
    IUnitOfWork unitOfWork)
{
    private static readonly UserPreferencesSettings NoPreferences = new(null, null, null);

    public async Task<SettingsDocumentResponse<UserPreferencesSettings>> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await settingsRepository.GetUserPreferencesAsync(requestContext.CompanyId, requestContext.UserId, cancellationToken);
        return stored is null
            ? new(NoPreferences, 0, false)
            : new(SettingsJson.Deserialize<UserPreferencesSettings>(stored.ValuesJson), stored.RowVersion, true);
    }

    public async Task<SettingsDocumentResponse<UserPreferencesSettings>> SaveAsync(
        SaveSettingsRequest<UserPreferencesSettings> request, CancellationToken cancellationToken)
    {
        var values = request.Values ?? throw new DomainException(ErrorKind.Validation, "The preferences are missing.");
        var normalized = UserPreferencesSettingsRules.Normalize(values);
        if (normalized.DefaultStoreId is { } storeId)
            await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);   // must be a real store of yours

        await settingsRepository.SaveUserPreferencesAsync(requestContext.CompanyId, requestContext.UserId,
            SettingsJson.Serialize(normalized), request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }
}
