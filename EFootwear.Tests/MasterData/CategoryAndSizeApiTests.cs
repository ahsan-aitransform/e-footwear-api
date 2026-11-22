using System.Net;
using System.Net.Http.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.MasterData;

/// <summary>Categories (docx 2.5: department, parent, unique name per department) and the sizes of a size group.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CategoryAndSizeApiTests(ApiTestEnvironment environment)
{
    private static readonly string DepartmentsUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Departments);
    private static readonly string CategoriesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Categories);
    private static readonly string SizeGroupsUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.SizeGroups);

    private static async Task<Guid> CreateDepartmentAsync(HttpClient client, string name) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, DepartmentsUrl, new { name })).GetProperty("id").GetGuid();

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, Guid departmentId, string name, Guid? parentCategoryId = null) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, CategoriesUrl, new { departmentId, name, parentCategoryId })).GetProperty("id").GetGuid();

    [Fact]
    public async Task A_category_name_is_unique_inside_its_department_only()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var shoes = await CreateDepartmentAsync(scenario.OwnerClient, "Shoes");
        var accessories = await CreateDepartmentAsync(scenario.OwnerClient, "Accessories");
        await CreateCategoryAsync(scenario.OwnerClient, shoes, "Casual");

        var duplicate = await scenario.OwnerClient.PostAsJsonAsync(CategoriesUrl, new { departmentId = shoes, name = "casual" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await scenario.OwnerClient.PostAsJsonAsync(CategoriesUrl, new { departmentId = accessories, name = "Casual" })).StatusCode);
    }

    [Fact]
    public async Task The_category_list_shows_department_names_and_filters_by_department_and_search()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var shoes = await CreateDepartmentAsync(scenario.OwnerClient, "Shoes");
        var accessories = await CreateDepartmentAsync(scenario.OwnerClient, "Accessories");
        await CreateCategoryAsync(scenario.OwnerClient, shoes, "Casual");
        await CreateCategoryAsync(scenario.OwnerClient, shoes, "Formal");
        await CreateCategoryAsync(scenario.OwnerClient, accessories, "Belts");

        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(CategoriesUrl));
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());

        var ofShoes = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CategoriesUrl}?departmentId={shoes}"));
        Assert.Equal(2, ofShoes.GetProperty("totalCount").GetInt32());
        Assert.All(ofShoes.GetProperty("items").EnumerateArray(), item => Assert.Equal("Shoes", item.GetProperty("departmentName").GetString()));

        var search = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CategoriesUrl}?searchText=accessories"));
        Assert.Equal(["Belts"], search.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());
    }

    [Fact]
    public async Task A_parent_category_must_be_in_the_same_department_and_may_not_create_a_loop()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var shoes = await CreateDepartmentAsync(scenario.OwnerClient, "Shoes");
        var accessories = await CreateDepartmentAsync(scenario.OwnerClient, "Accessories");
        var casual = await CreateCategoryAsync(scenario.OwnerClient, shoes, "Casual");
        var sneakers = await CreateCategoryAsync(scenario.OwnerClient, shoes, "Sneakers", casual);
        var belts = await CreateCategoryAsync(scenario.OwnerClient, accessories, "Belts");

        var wrongDepartment = await scenario.OwnerClient.PostAsJsonAsync(CategoriesUrl, new { departmentId = shoes, name = "Odd", parentCategoryId = belts });
        Assert.Equal(HttpStatusCode.BadRequest, wrongDepartment.StatusCode);

        var casualRow = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CategoriesUrl}/{casual}"));
        var ownParent = await scenario.OwnerClient.PutAsJsonAsync($"{CategoriesUrl}/{casual}", new
        {
            departmentId = shoes, name = "Casual", parentCategoryId = casual, nameUrdu = (string?)null, code = (string?)null,
            sortOrder = 0, isActive = true, rowVersion = casualRow.GetProperty("rowVersion").GetInt64(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, ownParent.StatusCode);

        // casual -> under its own child sneakers would be a loop
        var loop = await scenario.OwnerClient.PutAsJsonAsync($"{CategoriesUrl}/{casual}", new
        {
            departmentId = shoes, name = "Casual", parentCategoryId = sneakers, nameUrdu = (string?)null, code = (string?)null,
            sortOrder = 0, isActive = true, rowVersion = casualRow.GetProperty("rowVersion").GetInt64(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, loop.StatusCode);
    }

    [Fact]
    public async Task A_category_with_sub_categories_cannot_be_deleted_but_an_empty_one_can()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var shoes = await CreateDepartmentAsync(scenario.OwnerClient, "Shoes");
        var casual = await CreateCategoryAsync(scenario.OwnerClient, shoes, "Casual");
        var sneakers = await CreateCategoryAsync(scenario.OwnerClient, shoes, "Sneakers", casual);

        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.DeleteAsync($"{CategoriesUrl}/{casual}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{CategoriesUrl}/{sneakers}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{CategoriesUrl}/{casual}")).StatusCode);
    }

    [Fact]
    public async Task A_category_needs_an_active_department_of_the_same_company()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var departmentOfB = await CreateDepartmentAsync(companyB.OwnerClient, "Shoes of B");

        var crossCompany = await companyA.OwnerClient.PostAsJsonAsync(CategoriesUrl, new { departmentId = departmentOfB, name = "Intruder" });
        Assert.Equal(HttpStatusCode.BadRequest, crossCompany.StatusCode);
    }

    [Fact]
    public async Task Sizes_belong_to_a_size_group_have_unique_codes_and_can_be_deleted()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sizeGroupId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SizeGroupsUrl, new { name = "Men shoes EU" })).GetProperty("id").GetGuid();
        var sizesUrl = $"{SizeGroupsUrl}/{sizeGroupId}/sizes";

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, sizesUrl, new { sizeCode = "40", sortOrder = 2 });
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, sizesUrl, new { sizeCode = "39", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(sizesUrl, new { sizeCode = "40", sortOrder = 5 })).StatusCode);

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(sizesUrl));
        Assert.Equal(["39", "40"], list.EnumerateArray().Select(size => size.GetProperty("sizeCode").GetString()).ToArray());

        var sizeId = first.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.PutAsJsonAsync($"{sizesUrl}/{sizeId}", new { sizeCode = "41", sortOrder = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{sizesUrl}/{sizeId}")).StatusCode);
    }

    [Fact]
    public async Task Sizes_follow_the_permission_of_the_size_group_screen()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sizeGroupId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SizeGroupsUrl, new { name = "Kids" })).GetProperty("id").GetGuid();
        var sizesUrl = $"{SizeGroupsUrl}/{sizeGroupId}/sizes";

        var withoutPermission = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutPermission.GetAsync(sizesUrl)).StatusCode);

        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.SizeGroup.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(sizesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(sizesUrl, new { sizeCode = "S" })).StatusCode);
    }
}
