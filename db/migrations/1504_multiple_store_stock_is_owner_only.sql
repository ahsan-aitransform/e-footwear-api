-- 1504: Products Stock with Sizes on Multiple Stores is the company owner's alone (EFootwear.Domain OwnerOnlyPermissions). The owner holds it by
-- role, so every grant to a user or a role template is removed. The application already ignores such a grant; this keeps the stored rights true.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.
DELETE FROM sec.user_permissions u USING sec.users s
 WHERE s.company_id = u.company_id AND s.id = u.user_id AND s.role_code <> 'COMPANY_OWNER'
   AND u.permission_code LIKE 'PRODUCTS.PRODUCTS\_STOCK\_WITH\_SIZES\_ON\_MULTIPLE\_STORES.%';

DELETE FROM sec.role_template_permissions
 WHERE permission_code LIKE 'PRODUCTS.PRODUCTS\_STOCK\_WITH\_SIZES\_ON\_MULTIPLE\_STORES.%';
