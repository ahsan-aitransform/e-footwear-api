using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

internal static class ApiResponseExtensions
{
    public static async Task<JsonElement> ReadSuccessJsonAsync(this HttpResponseMessage response)
    {
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The response is the given error status with the given machine-readable error code (RFC 7807 problem details).</summary>
    public static async Task AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedErrorCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expectedStatus,
            $"Expected {(int)expectedStatus} but got {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(expectedErrorCode, problem.GetProperty("errorCode").GetString());
    }

    public static Task<HttpResponseMessage> PutSettingsAsync(this HttpClient client, string url, object values, long? expectedRowVersion = null) =>
        client.PutAsJsonAsync(url, new { values, expectedRowVersion });
}
