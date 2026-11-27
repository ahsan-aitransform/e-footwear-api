-- 1505: The screens that manage prices, discounts and promotions are the company owner's alone, viewing included (EFootwear.Domain
-- OwnerOnlyPermissions). Migration 1503 removed the right to change them; this removes the remaining view, print and export grants. Price bands
-- keep their reading rights. Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.
CREATE TEMPORARY TABLE owner_only_screen_permissions ON COMMIT DROP AS
SELECT code FROM sec.permissions
 WHERE code ~ '^(PRODUCTS.(CHANGE_PRICES_OLD_TO_NEW|CHANGE_PRICES_BY_PERCENTAGE|CHANGE_DISCOUNTS|PRODUCT_DISCOUNTS|PRODUCT_DISCOUNTS_ON_MULTIPLE_STORES|REMOVE_DISCOUNTS_AND_REVERSE_PRICE)|SETTINGS.(DEALS|SPECIAL_OFFERS|SPECIAL_OFFER_DETAILS|SALE_ON_STORES|SALE_ON_CATEGORIES)|PROMOTIONS.[A-Z_]+).';

DELETE FROM sec.user_permissions u USING sec.users s
 WHERE s.company_id = u.company_id AND s.id = u.user_id AND s.role_code <> 'COMPANY_OWNER'
   AND u.permission_code IN (SELECT code FROM owner_only_screen_permissions);

DELETE FROM sec.role_template_permissions WHERE permission_code IN (SELECT code FROM owner_only_screen_permissions);
