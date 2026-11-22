namespace EFootwear.Tests.Infrastructure;

/// <summary>Put <c>[Collection(ApiIntegrationCollection.Name)]</c> on an integration test class to share the API and its database.</summary>
[CollectionDefinition(Name)]
public sealed class ApiIntegrationCollection : ICollectionFixture<ApiTestEnvironment>
{
    public const string Name = "ApiIntegration";
}
