using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.MasterData;

/// <summary>
/// The simple master lists (docx 2.4 to 2.11 and 2.17), exercised through the departments and cities endpoints (every list uses
/// the same code): create, search, edit, soft delete, uniqueness, in-use protection, concurrency, permissions, tenant isolation.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class MasterListApiTests(ApiTestEnvironment environment)
{
    private static readonly string DepartmentsUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Departments.TrimStart('/'));
    private static readonly string CitiesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Cities.TrimStart('/'));
    private static readonly string CategoriesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Categories.TrimStart('/'));

    private static async Task<Guid> CreateItemAsync(HttpClient client, string url, string name)
    {
        var body = await OwnerCompanyScenario.PostJsonAsync(client, url, new { name });
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Owner_creates_lists_searches_and_edits_a_master_list_item()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var shoesId = await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "  Shoes   and   Sandals ");
        await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "Accessories");

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(DepartmentsUrl));
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
        var shoes = list.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == shoesId);
        Assert.Equal("Shoes and Sandals", shoes.GetProperty("name").GetString());   // spaces are normalised

        var search = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}?searchText=accessor"));
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());

        var update = await scenario.OwnerClient.PutAsJsonAsync($"{DepartmentsUrl}/{shoesId}", new
        {
            name = "Footwear", nameUrdu = "جوتے", code = "FW", sortOrder = 3, isActive = true, rowVersion = shoes.GetProperty("rowVersion").GetInt64(),
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var reloaded = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}/{shoesId}"));
        Assert.Equal("Footwear", reloaded.GetProperty("name").GetString());
        Assert.Equal("جوتے", reloaded.GetProperty("nameUrdu").GetString());
        Assert.Equal(3, reloaded.GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task A_name_must_be_unique_without_regard_to_case_and_is_free_again_after_deleting()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var itemId = await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "Kids");

        var duplicate = await scenario.OwnerClient.PostAsJsonAsync(DepartmentsUrl, new { name = "KIDS" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{DepartmentsUrl}/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await scenario.OwnerClient.PostAsJsonAsync(DepartmentsUrl, new { name = "Kids" })).StatusCode);

        // soft delete: the row is still in the table
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from master.departments where company_id = $1 and id = $2 and is_deleted", scenario.CompanyId, itemId));
    }

    [Fact]
    public async Task A_required_name_is_validated()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var empty = await scenario.OwnerClient.PostAsJsonAsync(DepartmentsUrl, new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await OwnerCompanyScenario.ReadJsonAsync(empty)).GetProperty("errorCode").GetString());

        var tooLong = await scenario.OwnerClient.PostAsJsonAsync(DepartmentsUrl, new { name = new string('x', 121) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task An_item_that_other_records_use_cannot_be_deleted_and_the_answer_says_where()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var departmentId = await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "Shoes");
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CategoriesUrl, new { departmentId, name = "Casual" });

        var delete = await scenario.OwnerClient.DeleteAsync($"{DepartmentsUrl}/{departmentId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(delete);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("master.categories", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_stale_row_version_is_refused_with_a_conflict()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var itemId = await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "Season Items");
        var loaded = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}/{itemId}"));
        var rowVersion = loaded.GetProperty("rowVersion").GetInt64();

        var first = await scenario.OwnerClient.PutAsJsonAsync($"{DepartmentsUrl}/{itemId}",
            new { name = "First edit", nameUrdu = (string?)null, code = (string?)null, sortOrder = 0, isActive = true, rowVersion });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await scenario.OwnerClient.PutAsJsonAsync($"{DepartmentsUrl}/{itemId}",
            new { name = "Second edit", nameUrdu = (string?)null, code = (string?)null, sortOrder = 0, isActive = true, rowVersion });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(second)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Saving_a_city_warns_about_a_name_that_looks_like_a_spelling_slip_but_still_saves()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateItemAsync(scenario.OwnerClient, CitiesUrl, "Shiekhupura");

        var second = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Sheikhpura" });
        var warnings = second.GetProperty("nearDuplicateNames").EnumerateArray().Select(name => name.GetString()).ToList();
        Assert.Contains("Shiekhupura", warnings);

        var unrelated = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Multan" });
        Assert.Empty(unrelated.GetProperty("nearDuplicateNames").EnumerateArray());
    }

    [Fact]
    public async Task Inactive_items_are_hidden_unless_asked_for_and_never_offered_as_options()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, DepartmentsUrl, new { name = "Active one", isActive = true });
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, DepartmentsUrl, new { name = "Dormant", isActive = false });

        var visible = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(DepartmentsUrl));
        Assert.Equal(1, visible.GetProperty("totalCount").GetInt32());
        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}?includeInactive=true"));
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());

        var options = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{DepartmentsUrl}/{ApiRoutes.MasterData.Options}"));
        Assert.Equal(["Active one"], options.EnumerateArray().Select(option => option.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public async Task Each_action_needs_its_own_permission_but_the_options_list_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateItemAsync(scenario.OwnerClient, DepartmentsUrl, "Shoes");

        var cashierWithNothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);
        var denied = await cashierWithNothing.GetAsync(DepartmentsUrl);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(ErrorCodes.PermissionDenied, (await OwnerCompanyScenario.ReadJsonAsync(denied)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await cashierWithNothing.GetAsync($"{DepartmentsUrl}/{ApiRoutes.MasterData.Options}")).StatusCode);

        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.Department.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(DepartmentsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(DepartmentsUrl, new { name = "Nope" })).StatusCode);

        var editor = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.Department.Add);
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync(DepartmentsUrl, new { name = "Allowed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync(DepartmentsUrl)).StatusCode);
    }

    [Fact]
    public async Task A_platform_administrator_is_refused_on_company_master_data()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(DepartmentsUrl)).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_master_data_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var departmentOfA = await CreateItemAsync(companyA.OwnerClient, DepartmentsUrl, "Secret department of A");

        var listOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync(DepartmentsUrl));
        Assert.Equal(0, listOfB.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{DepartmentsUrl}/{departmentOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{DepartmentsUrl}/{departmentOfA}")).StatusCode);

        // the same name is allowed in another company
        Assert.Equal(HttpStatusCode.Created, (await companyB.OwnerClient.PostAsJsonAsync(DepartmentsUrl, new { name = "Secret department of A" })).StatusCode);

        // and A's row is untouched
        var stillThere = await OwnerCompanyScenario.ReadJsonAsync(await companyA.OwnerClient.GetAsync($"{DepartmentsUrl}/{departmentOfA}"));
        Assert.Equal("Secret department of A", stillThere.GetProperty("name").GetString());
    }
}
