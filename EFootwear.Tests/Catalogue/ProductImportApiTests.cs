using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>New products from a file: everything or nothing, by the rules of the New Product screen.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductImportApiTests(ApiTestEnvironment environment)
{
    private const string Heading = "Product Name,Department,Category,Brand,Colour,Size,Barcode,Purchase Price,Sale Price";

    private static string Url(string route) => $"{ProductsUrl}/{route}";

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string route, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return await client.PostAsync(Url(route), content);
    }

    private static async Task<JsonElement> UploadCsvAsync(HttpClient client, string route, params string[] rows) =>
        await OwnerCompanyScenario.ReadJsonAsync(await UploadAsync(client, route, Encoding.UTF8.GetBytes(string.Join('\n', [Heading, .. rows])), "products.csv"));

    private static async Task<int> SizeCountAsync(HttpClient client) =>
        (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}"))).GetProperty("totalCount").GetInt32();

    [Fact]
    public async Task A_good_file_creates_every_product_once_with_its_sizes()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateLookupsAsync(scenario.OwnerClient);
        string[] rows = ["Alpha,Shoes,Casual,Bata,Black,40,ALPHA-40,500,999", "alpha,,,,Black,41,,500,999", "Beta,shoes,casual,,,7,,\"1,200\",1500"];

        var preview = await UploadCsvAsync(scenario.OwnerClient, ApiRoutes.Products.ImportPreview, rows);
        Assert.Empty(preview.GetProperty("problems").EnumerateArray());
        Assert.Equal(2, preview.GetProperty("products").GetArrayLength());
        Assert.Equal(0, await SizeCountAsync(scenario.OwnerClient));

        var imported = await UploadCsvAsync(scenario.OwnerClient, ApiRoutes.Products.Import, rows);
        Assert.Equal(2, imported.GetProperty("createdProductCount").GetInt32());
        Assert.Equal(3, await SizeCountAsync(scenario.OwnerClient));

        var again = await UploadCsvAsync(scenario.OwnerClient, ApiRoutes.Products.Import, rows);
        Assert.Equal(0, again.GetProperty("createdProductCount").GetInt32());
        Assert.NotEmpty(again.GetProperty("problems").EnumerateArray());
        Assert.Equal(3, await SizeCountAsync(scenario.OwnerClient));
    }

    [Fact]
    public async Task One_bad_row_creates_nothing_and_each_problem_names_its_row()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateLookupsAsync(scenario.OwnerClient);

        var result = await UploadCsvAsync(scenario.OwnerClient, ApiRoutes.Products.Import,
            "Good,Shoes,Casual,Bata,Black,40,SAME,500,999", "Bad,Shoes,Unknown,Nobody,Black,41,SAME,5.5,999");

        Assert.Equal(0, result.GetProperty("createdProductCount").GetInt32());
        var problems = result.GetProperty("problems").EnumerateArray().ToList();
        Assert.Equal(4, problems.Count); // category, brand, repeated barcode, price
        Assert.All(problems, problem => Assert.Equal(3, problem.GetProperty("rowNumber").GetInt32()));
        Assert.Equal(0, await SizeCountAsync(scenario.OwnerClient));
    }

    [Fact]
    public async Task The_template_is_an_excel_file_the_import_reads_and_a_wrong_column_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateLookupsAsync(scenario.OwnerClient);

        var template = await scenario.OwnerClient.GetByteArrayAsync(Url(ApiRoutes.Products.ImportTemplate));
        var headings = SpreadsheetReader.ReadRows(new MemoryStream(template), "template.xlsx").Single();
        Assert.Contains("Purchase Price", headings);

        var filled = SpreadsheetWriter.WriteWorkbook("Products",
        [
            headings,
            headings.Select(heading => heading switch { "Product Name" => "Gamma & Co", "Department" => "Shoes", "Category" => "Casual", "Size" => "42", "Purchase Price" => "100", "Sale Price" => "200", _ => "" }).ToList(),
        ]);
        var imported = await OwnerCompanyScenario.ReadJsonAsync(await UploadAsync(scenario.OwnerClient, ApiRoutes.Products.Import, filled, "filled.xlsx"));
        Assert.Equal(1, imported.GetProperty("createdProductCount").GetInt32());

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await UploadAsync(scenario.OwnerClient, ApiRoutes.Products.ImportPreview, Encoding.UTF8.GetBytes(Heading + ",Colr\nX,Shoes,Casual,,,1,,1,2,red"), "wrong.csv"));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(Url(ApiRoutes.Products.ImportTemplate)));
    }
}
