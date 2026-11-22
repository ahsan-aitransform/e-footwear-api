using E_Footwear_Api.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace E_Footwear_Api.Middleware;

/// <summary>
/// Adds a bearer-token definition to the OpenAPI document. This is what makes the "Authorize" button
/// appear in Swagger UI: paste the accessToken from /auth/login once and every call sends it.
/// Documentation only; real security is still enforced by the authentication handler.
/// </summary>
public sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    private const string SecuritySchemeName = HttpAuthorizationScheme.Bearer;
    private const string SecuritySchemeDescription = "Authorize here";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
                               CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = HttpAuthorizationScheme.Bearer.ToLowerInvariant(),
            Description = SecuritySchemeDescription,
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = [],
        });
        return Task.CompletedTask;
    }
}
