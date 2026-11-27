-- 1501: Search Customer, Search Vendor and Search Staff were merged into the View screens, so their permissions control nothing.
-- Their grants to users and role templates go with them. Nobody needs a replacement grant: searching is part of the View permission.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

DELETE FROM sec.user_permissions
 WHERE permission_code IN ('CUSTOMERS.SEARCH_CUSTOMER.VIEW', 'VENDORS.SEARCH_VENDOR.VIEW', 'HUMAN_RESOURCES.SEARCH_STAFF.VIEW');

DELETE FROM sec.role_template_permissions
 WHERE permission_code IN ('CUSTOMERS.SEARCH_CUSTOMER.VIEW', 'VENDORS.SEARCH_VENDOR.VIEW', 'HUMAN_RESOURCES.SEARCH_STAFF.VIEW');

DELETE FROM sec.permissions
 WHERE code IN ('CUSTOMERS.SEARCH_CUSTOMER.VIEW', 'VENDORS.SEARCH_VENDOR.VIEW', 'HUMAN_RESOURCES.SEARCH_STAFF.VIEW');
