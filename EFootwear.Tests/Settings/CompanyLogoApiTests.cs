using System.Net;
using System.Net.Http.Headers;
using EFootwear.Domain.Core;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>The company logo: real image bytes only, a size limit, history kept, files only ever served to their own company.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CompanyLogoApiTests(ApiTestEnvironment environment)
{
    private static byte[] PngBytes(int length = 64) =>
        [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. Enumerable.Repeat((byte)7, length - 8)];

    private static byte[] JpegBytes() => [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8];

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string declaredType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredType);
        form.Add(file, "file", fileName);
        return form;
    }

    [Fact]
    public async Task A_logo_is_uploaded_served_replaced_and_its_history_is_kept()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyLogoInformation)).StatusCode);
        await (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyLogo)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");

        var png = PngBytes();
        var uploaded = await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload(png, "logo.png", "image/png"))).ReadSuccessJsonAsync();
        Assert.Equal("image/png", uploaded.GetProperty("contentType").GetString());
        Assert.Equal(png.Length, uploaded.GetProperty("sizeBytes").GetInt32());

        var content = await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyLogo);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(png, await content.Content.ReadAsByteArrayAsync());

        var folder = Path.Combine(environment.ApiFactory.FileStorageDirectory, "companies", scenario.Company.CompanyId.ToString("N"), "logo");
        Assert.Single(Directory.GetFiles(folder));

        var jpeg = JpegBytes();
        await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload(jpeg, "logo.jpg", "image/jpeg"))).ReadSuccessJsonAsync();
        var current = await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyLogo);
        Assert.Equal("image/jpeg", current.Content.Headers.ContentType?.MediaType);
        Assert.Equal(jpeg, await current.Content.ReadAsByteArrayAsync());

        Assert.Equal(2, Directory.GetFiles(folder).Length);
        Assert.Equal(2L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from core.attachments where company_id = $1 and entity_type = 'company_logo'", scenario.Company.CompanyId));
    }

    [Fact]
    public async Task Only_real_images_within_the_size_limit_are_accepted_whatever_the_client_declares()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload("<script>alert(1)</script>"u8.ToArray(), "logo.png", "image/png")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "UNSUPPORTED_FILE_TYPE");
        await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "logo.svg", "image/svg+xml")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "UNSUPPORTED_FILE_TYPE");
        await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload([], "empty.png", "image/png")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload(PngBytes(AttachmentRules.LogoMaximumSizeBytes + 10), "big.png", "image/png")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "FILE_TOO_LARGE");

        var exactlyAtTheLimit = PngBytes(AttachmentRules.LogoMaximumSizeBytes);
        Assert.Equal(HttpStatusCode.OK, (await scenario.OwnerClient.PutAsync(SettingsUrls.CompanyLogo, Upload(exactlyAtTheLimit, "limit.png", "image/png"))).StatusCode);
    }

    [Fact]
    public async Task Everybody_sees_their_own_logo_only_the_profile_editor_uploads_and_companies_stay_apart()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        var cashier = await first.CreateUserClientAsync("CASHIER_SALESMAN", [first.Company.FirstStoreId]);
        var editor = await first.CreateUserClientAsync("STORE_MANAGER", [first.Company.FirstStoreId], PermissionCodes.Settings.CompanyProfile.Edit);

        await (await cashier.PutAsync(SettingsUrls.CompanyLogo, Upload(PngBytes(), "logo.png", "image/png")))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        await (await editor.PutAsync(SettingsUrls.CompanyLogo, Upload(PngBytes(), "logo.png", "image/png"))).ReadSuccessJsonAsync();

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(SettingsUrls.CompanyLogo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(SettingsUrls.CompanyLogoInformation)).StatusCode);

        await (await second.OwnerClient.GetAsync(SettingsUrls.CompanyLogo)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(HttpStatusCode.NoContent, (await second.OwnerClient.GetAsync(SettingsUrls.CompanyLogoInformation)).StatusCode);
        await (await first.PlatformClient.GetAsync(SettingsUrls.CompanyLogo)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
