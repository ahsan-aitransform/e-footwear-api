-- 1502_remove_search_product_permission
-- The Search Product screen is removed; View Products lists and searches the products. Whoever (user or role template) held its permission gets
-- View Products: View instead, and the old permission is deleted (a code that is no longer in the catalog could not be saved again).
INSERT INTO sec.user_permissions (company_id, user_id, permission_code, granted_by, granted_at)
SELECT company_id, user_id, 'PRODUCTS.VIEW_PRODUCTS.VIEW', granted_by, granted_at
  FROM sec.user_permissions
 WHERE permission_code = 'PRODUCTS.SEARCH_PRODUCT.VIEW'
   AND EXISTS (SELECT 1 FROM sec.permissions WHERE code = 'PRODUCTS.VIEW_PRODUCTS.VIEW')
ON CONFLICT DO NOTHING;

INSERT INTO sec.role_template_permissions (company_id, template_id, permission_code)
SELECT company_id, template_id, 'PRODUCTS.VIEW_PRODUCTS.VIEW'
  FROM sec.role_template_permissions
 WHERE permission_code = 'PRODUCTS.SEARCH_PRODUCT.VIEW'
   AND EXISTS (SELECT 1 FROM sec.permissions WHERE code = 'PRODUCTS.VIEW_PRODUCTS.VIEW')
ON CONFLICT DO NOTHING;

DELETE FROM sec.user_permissions WHERE permission_code = 'PRODUCTS.SEARCH_PRODUCT.VIEW';
DELETE FROM sec.role_template_permissions WHERE permission_code = 'PRODUCTS.SEARCH_PRODUCT.VIEW';
DELETE FROM sec.permissions WHERE code = 'PRODUCTS.SEARCH_PRODUCT.VIEW';
