using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for company role templates (sec.role_templates and its permission rows).</summary>
public interface IRoleTemplateRepository
{
    Task<IReadOnlyList<RoleTemplateSummaryResponse>> ListAsync(CancellationToken cancellationToken);

    Task<RoleTemplateDetailsResponse?> FindAsync(Guid templateId, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, Guid? exceptTemplateId, CancellationToken cancellationToken);

    Task<Guid> InsertAsync(Guid companyId, string name, string? description, IReadOnlyCollection<string> permissionCodes,
                           Guid createdByUserId, CancellationToken cancellationToken);

    /// <summary>Throws a conflict when <paramref name="expectedRowVersion"/> is stale.</summary>
    Task UpdateAsync(Guid companyId, Guid templateId, string name, string? description,
                     IReadOnlyCollection<string> permissionCodes, long expectedRowVersion, CancellationToken cancellationToken);

    Task DeleteAsync(Guid companyId, Guid templateId, CancellationToken cancellationToken);
}
