using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>Per-user permissions (Part 6, docx 1.2) and role templates: replace sets, no escalation, owner protection, isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class UserPermissionsApiTests(ApiTestEnvironment environment)
{
    private static string[] CodesOf(System.Text.Json.JsonElement body, string property) =>
        body.GetProperty(property).EnumerateArray().Select(code => code.GetString()!).ToArray();

    [Fact]
    public async Task The_owner_holds_the_whole_catalog_by_role_and_cannot_be_reduced()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var ownerId = await company.QueryScalarAsync<Guid>(
            "select id from sec.users where company_id = $1 and role_code = 'COMPANY_OWNER'", company.CompanyId);

        var body = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(ownerId, "permissions")));

        Assert.True(body.GetProperty("holdsEveryPermissionByRole").GetBoolean());
        Assert.Equal(PermissionCatalog.AllPermissionCodes.Count, CodesOf(body, "grantedPermissionCodes").Length);
        var reduce = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(ownerId, "permissions"),
            new { permissionCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Forbidden, reduce.StatusCode);
    }

    [Fact]
    public async Task Replacing_a_permission_set_writes_only_the_difference_and_logs_one_summary_line()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);
        string[] newSet = [PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.Products.NewProduct.View];

        var response = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"),
            new { permissionCodes = newSet });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var granted = CodesOf(await SecurityTestCompany.ReadAsync(
            await company.OwnerClient.GetAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"))), "grantedPermissionCodes");
        Assert.Equal(newSet.Order().ToArray(), granted);
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and action = $2 and entity_id = $3 and remarks like '%removed%'",
            company.CompanyId, SecurityActivityActions.PermissionsChanged, cashier.UserId.ToString()));

        // sending the same set again changes nothing and logs nothing new
        var again = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"), new { permissionCodes = newSet });
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(1L, await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and action = $2 and entity_id = $3 and remarks like '%removed%'",
            company.CompanyId, SecurityActivityActions.PermissionsChanged, cashier.UserId.ToString()));
    }

    [Fact]
    public async Task The_new_permissions_apply_to_the_users_next_request()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (cashier, cashierClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashierClient.GetAsync(SecurityTestCompany.UsersUrl())).StatusCode);

        await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"),
            new { permissionCodes = new[] { PermissionCodes.UserAccount.ViewUsers.View } });

        Assert.Equal(HttpStatusCode.OK, (await cashierClient.GetAsync(SecurityTestCompany.UsersUrl())).StatusCode);
    }

    [Fact]
    public async Task An_unknown_permission_code_is_refused_with_400()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        var response = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"),
            new { permissionCodes = new[] { "MADE.UP.CODE" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_person_cannot_grant_a_permission_they_do_not_hold_and_cannot_change_their_own_permissions()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (manager, managerClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.UserAccount.RolesAndPermissions.View,
            PermissionCodes.UserAccount.RolesAndPermissions.Edit);
        var cashier = await company.CreateUserAsync(RoleCodes.CashierSalesman);

        // The suggested cashier set has no product deletion, and neither does this manager.
        var escalation = await managerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"),
            new { permissionCodes = new[] { PermissionCodes.UserAccount.ViewUsers.View, PermissionCodes.Products.NewProduct.Delete } });
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);

        var allowed = await managerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(cashier.UserId, "permissions"),
            new { permissionCodes = new[] { PermissionCodes.UserAccount.ViewUsers.View } });
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

        var ownPermissions = await managerClient.PutAsJsonAsync(SecurityTestCompany.UserUrl(manager.UserId, "permissions"),
            new { permissionCodes = new[] { PermissionCodes.UserAccount.ViewUsers.View } });
        Assert.Equal(HttpStatusCode.Forbidden, ownPermissions.StatusCode);
    }

    [Fact]
    public async Task Role_templates_list_the_system_suggestions_first_and_a_suggestion_is_read_only()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);

        var list = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl()));
        var suggestions = list.EnumerateArray().Where(template => template.GetProperty("isSystemSuggestion").GetBoolean()).ToList();
        Assert.Equal(3, suggestions.Count);
        Assert.All(suggestions, suggestion => Assert.True(suggestion.GetProperty("permissionCount").GetInt32() > 0));

        var cashier = await SecurityTestCompany.ReadAsync(
            await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl(RoleCodes.CashierSalesman)));
        Assert.Contains(PermissionCodes.Invoice.NewSale.Add, CodesOf(cashier, "permissionCodes"));
        Assert.Equal(HttpStatusCode.NotFound, (await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl("NOT_A_ROLE"))).StatusCode);
    }

    [Fact]
    public async Task A_company_role_template_can_be_created_read_changed_and_deleted()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        string[] codes = [PermissionCodes.Products.NewProduct.View, PermissionCodes.Products.NewProduct.Add];

        var created = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(),
            new { name = "Stock keeper", description = "Looks after products", permissionCodes = codes });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var templateId = (await SecurityTestCompany.ReadAsync(created)).GetProperty("id").GetGuid();

        var details = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString())));
        Assert.Equal("Stock keeper", details.GetProperty("name").GetString());
        Assert.Equal(codes.Order().ToArray(), CodesOf(details, "permissionCodes"));
        var rowVersion = details.GetProperty("rowVersion").GetInt64();

        var updated = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()), new
        {
            name = "Stock keeper v2", description = (string?)null, permissionCodes = new[] { PermissionCodes.Products.NewProduct.View }, rowVersion,
        });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var stale = await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()), new
        {
            name = "Stock keeper v3", permissionCodes = new[] { PermissionCodes.Products.NewProduct.View }, rowVersion,
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var reread = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString())));
        Assert.Equal([PermissionCodes.Products.NewProduct.View], CodesOf(reread, "permissionCodes"));

        Assert.Equal(HttpStatusCode.NoContent, (await company.OwnerClient.DeleteAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await company.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()))).StatusCode);
        Assert.Equal(0L, await company.QueryScalarAsync<long>(
            "select count(*) from sec.role_template_permissions where company_id = $1 and template_id = $2", company.CompanyId, templateId));
    }

    [Fact]
    public async Task Role_template_names_are_unique_per_company_and_codes_must_exist()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var body = new { name = "Cashier plus", permissionCodes = new[] { PermissionCodes.Products.NewProduct.View } };
        Assert.Equal(HttpStatusCode.Created, (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(), body)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(), new { name = "CASHIER PLUS", permissionCodes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(), new { name = "Bad", permissionCodes = new[] { "NO.SUCH.CODE" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(), new { name = "   ", permissionCodes = Array.Empty<string>() })).StatusCode);
    }

    [Fact]
    public async Task Role_templates_belong_to_one_company_and_need_the_roles_permission()
    {
        var companyA = await SecurityTestCompany.CreateAsync(environment);
        var companyB = await SecurityTestCompany.CreateAsync(environment);
        var created = await companyA.OwnerClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(),
            new { name = "Private", permissionCodes = Array.Empty<string>() });
        var templateId = (await SecurityTestCompany.ReadAsync(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync(SecurityTestCompany.RoleTemplatesUrl(templateId.ToString()))).StatusCode);
        Assert.DoesNotContain("Private", await companyB.OwnerClient.GetStringAsync(SecurityTestCompany.RoleTemplatesUrl()));

        var (_, noPermissionClient) = await companyA.CreateSignedInUserAsync(RoleCodes.CashierSalesman);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermissionClient.GetAsync(SecurityTestCompany.RoleTemplatesUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPermissionClient.PostAsJsonAsync(SecurityTestCompany.RoleTemplatesUrl(), new { name = "x", permissionCodes = Array.Empty<string>() })).StatusCode);
    }
}
