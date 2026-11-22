using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Role templates (Part 6): named permission sets that PREFILL a user's permissions. A template never limits a user: after applying one,
/// every permission can still be changed one by one. Two kinds are offered together:
///  * system suggestions, one per role (DefaultRolePermissionTemplates, labelled as SUGGESTIONS: which role may do what is a
///    [BUSINESS DECISION REQUIRED]); they are read-only;
///  * company templates the owner creates, edits and deletes.
/// </summary>
public sealed class RoleTemplateService(
    IRoleTemplateRepository roleTemplateRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<RoleTemplateService> logger)
{
    private const string SuggestionDescription = "Suggested starting set. Not a business rule: change any permission after applying it.";

    private static readonly IReadOnlyDictionary<string, string> SuggestionNamesByRoleCode = new Dictionary<string, string>
    {
        [RoleCodes.StoreManager] = "Store manager (suggested)",
        [RoleCodes.CashierSalesman] = "Cashier / salesman (suggested)",
        [RoleCodes.Accountant] = "Accountant (suggested)",
    };

    public async Task<IReadOnlyList<RoleTemplateSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var suggestions = SuggestionNamesByRoleCode.Select(pair => new RoleTemplateSummaryResponse(
            pair.Key, null, pair.Value, SuggestionDescription, IsSystemSuggestion: true,
            DefaultRolePermissionTemplates.PermissionCodesForRole(pair.Key).Count));
        var companyTemplates = await roleTemplateRepository.ListAsync(cancellationToken);
        return [.. suggestions, .. companyTemplates];
    }

    /// <summary>A template by its key: a role code for a suggestion, or the template id as text.</summary>
    public async Task<RoleTemplateDetailsResponse> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (SuggestionNamesByRoleCode.TryGetValue(key, out var suggestionName))
            return new RoleTemplateDetailsResponse(key, null, suggestionName, SuggestionDescription, IsSystemSuggestion: true,
                DefaultRolePermissionTemplates.PermissionCodesForRole(key), RowVersion: 0);

        if (!Guid.TryParse(key, out var templateId))
            throw new DomainException(ErrorKind.NotFound, "Role template not found.");
        return await roleTemplateRepository.FindAsync(templateId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Role template not found.");
    }

    public async Task<Guid> CreateAsync(SaveRoleTemplateRequest request, CancellationToken cancellationToken)
    {
        var (name, description, permissionCodes) = Validate(request);
        if (await roleTemplateRepository.NameExistsAsync(name, null, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"A role template named {name} already exists.");

        var templateId = await roleTemplateRepository.InsertAsync(requestContext.CompanyId, name, description, permissionCodes,
                                                                  requestContext.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Role template {TemplateId} created by {ActorUserId}", templateId, requestContext.UserId);
        return templateId;
    }

    public async Task UpdateAsync(Guid templateId, SaveRoleTemplateRequest request, CancellationToken cancellationToken)
    {
        var (name, description, permissionCodes) = Validate(request);
        if (request.RowVersion is null)
            throw new DomainException(ErrorKind.Validation, "The row version of the template is required to save a change.");
        if (await roleTemplateRepository.FindAsync(templateId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "Role template not found.");
        if (await roleTemplateRepository.NameExistsAsync(name, templateId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"A role template named {name} already exists.");

        await roleTemplateRepository.UpdateAsync(requestContext.CompanyId, templateId, name, description, permissionCodes,
                                                 request.RowVersion.Value, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Role template {TemplateId} updated by {ActorUserId}", templateId, requestContext.UserId);
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        if (await roleTemplateRepository.FindAsync(templateId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "Role template not found.");
        await roleTemplateRepository.DeleteAsync(requestContext.CompanyId, templateId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Role template {TemplateId} deleted by {ActorUserId}", templateId, requestContext.UserId);
    }

    private static (string Name, string? Description, IReadOnlyList<string> PermissionCodes) Validate(SaveRoleTemplateRequest request)
    {
        var name = SecurityLimits.EnsureRequiredText(request.Name, "Name", SecurityLimits.MaximumRoleTemplateNameLength);
        var description = SecurityLimits.EnsureOptionalText(request.Description, "Description", SecurityLimits.MaximumDescriptionLength);
        var permissionCodes = (request.PermissionCodes ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        PermissionGrantRules.EnsureCodesAreKnown(permissionCodes);
        return (name, description, permissionCodes);
    }
}
