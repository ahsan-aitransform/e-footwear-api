using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;

namespace EFootwear.Tests.Security;

/// <summary>Managing prices and discounts is the company owner's alone.</summary>
public class OwnerOnlyPermissionsTests
{
    [Theory]
    [InlineData("PRICE_CHANGE")]
    [InlineData("DISCOUNT_MANUAL")]
    [InlineData("INVOICE.NEW_SALE.GIVE_DISCOUNT")]
    [InlineData("PRODUCTS.NEW_PRODUCT.CHANGE_SELLING_PRICE")]
    [InlineData("PRODUCTS.CHANGE_DISCOUNTS.EDIT")]
    [InlineData("SETTINGS.SPECIAL_OFFERS.ADD")]
    [InlineData("PROMOTIONS.PROMOTION_CONFLICT_RESOLUTION.APPROVE")]
    public void Price_and_discount_management_is_owner_only_and_cannot_be_granted(string permissionCode)
    {
        Assert.True(OwnerOnlyPermissions.Contains(permissionCode));
        Assert.Throws<DomainException>(() => PermissionGrantRules.EnsureNotOwnerOnly([permissionCode]));
    }

    [Theory]
    [InlineData("PRODUCTS.NEW_PRODUCT.ADD")]
    [InlineData("PRODUCTS.CHANGE_DISCOUNTS.VIEW")]
    [InlineData("SALE_CREATE")]
    [InlineData("INVOICE.NEW_SALE.ADD")]
    public void Selling_adding_products_and_looking_at_discounts_stay_open_to_others(string permissionCode) =>
        Assert.False(OwnerOnlyPermissions.Contains(permissionCode));

    [Fact]
    public void No_suggested_role_but_the_owner_gets_an_owner_only_permission()
    {
        foreach (var roleCode in new[] { RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant })
            Assert.DoesNotContain(DefaultRolePermissionTemplates.PermissionCodesForRole(roleCode), OwnerOnlyPermissions.Contains);
    }
}
