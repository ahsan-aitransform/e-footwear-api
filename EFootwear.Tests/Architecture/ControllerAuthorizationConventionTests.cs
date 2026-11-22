using System.Reflection;
using E_Footwear_Api.Authorization;
using E_Footwear_Api.Controllers.Security;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EFootwear.Tests.Architecture;

/// <summary>
/// Rule from Part 6 / 34: the backend enforces permissions even when a screen is hidden. These tests read every controller of the
/// API and fail when a new endpoint forgets its protection, so nobody has to remember to review it by hand.
/// </summary>
public class ControllerAuthorizationConventionTests
{
    // Endpoints that any signed-in person may call without a permission code. Adding to this list is a conscious decision.
    private static readonly HashSet<string> AuthenticatedOnlyEndpoints =
    [
        "AuthenticationController.Logout",
        "AuthenticationController.GetCurrentSession",
        "PermissionsController.GetCatalog",
    ];

    private static IEnumerable<(Type Controller, MethodInfo Action)> AllActions() =>
        typeof(AuthenticationController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
                .Select(method => (controller, method)));

    private static string Name((Type Controller, MethodInfo Action) endpoint) => $"{endpoint.Controller.Name}.{endpoint.Action.Name}";

    private static IEnumerable<T> Attributes<T>((Type Controller, MethodInfo Action) endpoint) where T : Attribute =>
        endpoint.Action.GetCustomAttributes<T>(inherit: true).Concat(endpoint.Controller.GetCustomAttributes<T>(inherit: true));

    [Fact]
    public void There_are_controllers_to_check()
    {
        Assert.True(AllActions().Count() >= 14);
    }

    [Fact]
    public void Every_endpoint_is_either_anonymous_or_requires_authorization()
    {
        foreach (var endpoint in AllActions())
        {
            var isAnonymous = endpoint.Action.GetCustomAttributes<AllowAnonymousAttribute>().Any();
            var requiresAuthorization = Attributes<AuthorizeAttribute>(endpoint).Any();
            Assert.True(isAnonymous || requiresAuthorization, $"{Name(endpoint)} has neither [Authorize] nor [AllowAnonymous].");
        }
    }

    [Fact]
    public void Only_the_sign_in_and_password_reset_endpoints_are_anonymous()
    {
        var anonymous = AllActions()
            .Where(endpoint => endpoint.Action.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .Select(Name).Order().ToArray();
        Assert.Equal(
        [
            "AuthenticationController.CompletePasswordReset", "AuthenticationController.Login",
            "AuthenticationController.RequestPasswordReset", "PlatformAdministratorAuthenticationController.Login",
        ], anonymous);
    }

    [Fact]
    public void Every_company_endpoint_needs_a_permission_or_is_on_the_conscious_allow_list()
    {
        foreach (var endpoint in AllActions().Where(candidate => !candidate.Action.GetCustomAttributes<AllowAnonymousAttribute>().Any()))
        {
            var policies = Attributes<AuthorizeAttribute>(endpoint).Select(attribute => attribute.Policy).Where(policy => policy is not null).ToList();
            var hasPermission = Attributes<RequirePermissionAttribute>(endpoint).Any();
            var isPlatformEndpoint = policies.Contains(AuthorizationPolicies.PlatformAdministratorOnly);
            var isCompanyOnlyEndpoint = policies.Contains(AuthorizationPolicies.CompanyUserOnly);
            var isAllowed = AuthenticatedOnlyEndpoints.Contains(Name(endpoint));

            Assert.True(hasPermission || isPlatformEndpoint || isCompanyOnlyEndpoint || isAllowed,
                $"{Name(endpoint)} is only [Authorize]d. Add [RequirePermission(PermissionCodes....)], a policy from AuthorizationPolicies, or (rarely) add it to the allow list of this test.");
        }
    }

    [Fact]
    public void Every_platform_controller_uses_the_platform_administrator_policy()
    {
        foreach (var endpoint in AllActions().Where(candidate => candidate.Controller.Namespace!.EndsWith(".Platform")
                                                                 && !candidate.Action.GetCustomAttributes<AllowAnonymousAttribute>().Any()))
        {
            Assert.Contains(AuthorizationPolicies.PlatformAdministratorOnly,
                Attributes<AuthorizeAttribute>(endpoint).Select(attribute => attribute.Policy));
        }
    }

    [Fact]
    public void Every_permission_code_used_by_an_endpoint_is_in_the_catalog()
    {
        var used = AllActions().SelectMany(endpoint => Attributes<RequirePermissionAttribute>(endpoint)).Select(attribute => attribute.PermissionCode).Distinct().ToList();
        Assert.NotEmpty(used);
        foreach (var code in used) Assert.Contains(code, PermissionCatalog.AllPermissionCodes);
    }
}
