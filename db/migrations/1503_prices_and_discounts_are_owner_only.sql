-- 1503: Only the company owner manages prices and discounts (EFootwear.Domain OwnerOnlyPermissions). The owner holds every permission by role,
-- so every grant of these to a user or a role template is removed. The database functions of a sale check the same rows, so a manual price
-- change or discount at the till is refused for everyone else from here on.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.
CREATE TEMPORARY TABLE owner_only_permissions ON COMMIT DROP AS
SELECT code FROM sec.permissions
 WHERE code IN ('PRICE_CHANGE', 'DISCOUNT_MANUAL', 'DISCOUNT_APPROVE')
    OR code ~ '\.(CHANGE_SELLING_PRICE|GIVE_DISCOUNT)$'
    OR (code ~ '^(PRODUCTS\.(CHANGE_PRICES_OLD_TO_NEW|CHANGE_PRICES_BY_PERCENTAGE|CHANGE_DISCOUNTS|PRODUCT_DISCOUNTS|PRODUCT_DISCOUNTS_ON_MULTIPLE_STORES|REMOVE_DISCOUNTS_AND_REVERSE_PRICE|PRICE_BANDS)|SETTINGS\.(DEALS|SPECIAL_OFFERS|SPECIAL_OFFER_DETAILS|SALE_ON_STORES|SALE_ON_CATEGORIES)|PROMOTIONS\.[A-Z_]+)\.'
        AND code !~ '\.(VIEW|PRINT|EXPORT)$');

DELETE FROM sec.user_permissions u USING sec.users s
 WHERE s.company_id = u.company_id AND s.id = u.user_id AND s.role_code <> 'COMPANY_OWNER'
   AND u.permission_code IN (SELECT code FROM owner_only_permissions);

DELETE FROM sec.role_template_permissions WHERE permission_code IN (SELECT code FROM owner_only_permissions);
