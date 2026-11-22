using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>DAL for company role templates (sec.role_templates, sec.role_template_permissions). Row-level security limits every query to the caller's company.</summary>
public sealed class RoleTemplateRepository(DatabaseSession databaseSession) : IRoleTemplateRepository
{
    public async Task<IReadOnlyList<RoleTemplateSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var templates = await databaseContext.RoleTemplates.AsNoTracking().OrderBy(template => template.Name).ToListAsync(cancellationToken);
        var codeCounts = await databaseContext.RoleTemplatePermissions.AsNoTracking()
            .GroupBy(permission => permission.TemplateId)
            .Select(group => new { TemplateId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TemplateId, row => row.Count, cancellationToken);

        return templates
            .Select(template => new RoleTemplateSummaryResponse(template.Id.ToString(), template.Id, template.Name, template.Description,
                IsSystemSuggestion: false, codeCounts.GetValueOrDefault(template.Id)))
            .ToList();
    }

    public async Task<RoleTemplateDetailsResponse?> FindAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var template = await databaseContext.RoleTemplates.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);
        if (template is null) return null;

        var codes = await databaseContext.RoleTemplatePermissions.AsNoTracking()
            .Where(permission => permission.TemplateId == templateId)
            .Select(permission => permission.PermissionCode)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
        return new RoleTemplateDetailsResponse(template.Id.ToString(), template.Id, template.Name, template.Description,
                                               IsSystemSuggestion: false, codes, template.RowVersion);
    }

    public async Task<bool> NameExistsAsync(string name, Guid? exceptTemplateId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var lowerCaseName = name.ToLower();
        return await databaseContext.RoleTemplates.AsNoTracking()
            .AnyAsync(template => template.Name.ToLower() == lowerCaseName && (exceptTemplateId == null || template.Id != exceptTemplateId),
                      cancellationToken);
    }

    public async Task<Guid> InsertAsync(Guid companyId, string name, string? description, IReadOnlyCollection<string> permissionCodes,
                                        Guid createdByUserId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var templateId = Guid.CreateVersion7();
        databaseContext.RoleTemplates.Add(new RoleTemplate
        {
            CompanyId = companyId, Id = templateId, Name = name, Description = description, CreatedBy = createdByUserId,
        });
        databaseContext.RoleTemplatePermissions.AddRange(permissionCodes.Select(code =>
            new RoleTemplatePermission { CompanyId = companyId, TemplateId = templateId, PermissionCode = code }));
        return templateId;
    }

    public async Task UpdateAsync(Guid companyId, Guid templateId, string name, string? description,
                                  IReadOnlyCollection<string> permissionCodes, long expectedRowVersion,
                                  CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var template = await databaseContext.RoleTemplates.SingleOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Role template not found.");

        databaseContext.Entry(template).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;
        template.Name = name;
        template.Description = description;

        await databaseContext.RoleTemplatePermissions
            .Where(permission => permission.TemplateId == templateId)
            .ExecuteDeleteAsync(cancellationToken);
        databaseContext.RoleTemplatePermissions.AddRange(permissionCodes.Select(code =>
            new RoleTemplatePermission { CompanyId = companyId, TemplateId = templateId, PermissionCode = code }));
    }

    public async Task DeleteAsync(Guid companyId, Guid templateId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        // The permission rows go with it (ON DELETE CASCADE).
        await databaseContext.RoleTemplates.Where(template => template.CompanyId == companyId && template.Id == templateId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
