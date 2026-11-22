namespace EFootwear.BLL.Core.Abstractions;

/// <summary>
/// Storage of settings sections (core.company_settings, core.store_settings, core.user_preferences). Implemented in
/// EFootwear.DAL/Core/Repositories/SettingsRepository.cs. A save with an expected row version that no longer matches
/// throws a DomainException with the CONCURRENT_UPDATE code.
/// </summary>
public interface ISettingsRepository
{
    Task<StoredSettingsSection?> GetCompanySectionAsync(Guid companyId, string sectionCode, CancellationToken cancellationToken);

    Task SaveCompanySectionAsync(Guid companyId, string sectionCode, string valuesJson, long? expectedRowVersion,
                                 CancellationToken cancellationToken);

    Task<StoredSettingsSection?> GetStoreSectionAsync(Guid companyId, Guid storeId, string sectionCode, CancellationToken cancellationToken);

    Task SaveStoreSectionAsync(Guid companyId, Guid storeId, string sectionCode, string valuesJson, long? expectedRowVersion,
                               CancellationToken cancellationToken);

    Task<StoredSettingsSection?> GetUserPreferencesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);

    Task SaveUserPreferencesAsync(Guid companyId, Guid userId, string valuesJson, long? expectedRowVersion,
                                  CancellationToken cancellationToken);
}
