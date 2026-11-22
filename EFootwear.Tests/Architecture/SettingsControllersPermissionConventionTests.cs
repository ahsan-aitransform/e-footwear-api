using System.Reflection;
using E_Footwear_Api.Authorization;
using E_Footwear_Api.Controllers.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EFootwear.Tests.Architecture;

/// <summary>
/// Settings and store endpoints of the Core module: every action needs a screen permission, except this short conscious list of
/// endpoints that any company user may call for their own use (payment methods to sell, own preferences, the logo to display).
/// </summary>
public class SettingsControllersPermissionConventionTests
{
    private static readonly HashSet<string> AnyCompanyUserEndpoints =
    [
        "CompanySettingsController.GetEnabledPaymentMethods",
        "CompanyLogoController.GetInformation",
        "CompanyLogoController.GetContent",
        "UserPreferencesController.Get",
        "UserPreferencesController.Save",
        "StoresController.List",
    ];

    [Fact]
    public void Every_core_endpoint_needs_a_permission_or_is_on_the_conscious_list()
    {
        var actions = typeof(CompanySettingsController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract && type.Namespace == typeof(CompanySettingsController).Namespace)
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
                .Select(method => (Controller: controller, Method: method)))
            .ToList();

        Assert.True(actions.Count >= 25);
        foreach (var (controller, method) in actions)
        {
            var name = $"{controller.Name}.{method.Name}";
            var hasPermission = method.GetCustomAttributes<RequirePermissionAttribute>().Any()
                                || controller.GetCustomAttributes<RequirePermissionAttribute>().Any();
            Assert.True(hasPermission || AnyCompanyUserEndpoints.Contains(name),
                $"{name} has no [RequirePermission] and is not on the conscious list of this test.");
            Assert.Contains(AuthorizationPolicies.CompanyUserOnly,
                controller.GetCustomAttributes<AuthorizeAttribute>().Select(attribute => attribute.Policy));
        }
    }
}
