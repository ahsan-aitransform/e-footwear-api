-- Down of 1501. The three permissions come back; who had them is not recorded, so grant them again where needed.
INSERT INTO sec.permissions (code, description, module_code, screen_code, action_code, sort_order) VALUES
    ('CUSTOMERS.SEARCH_CUSTOMER.VIEW', 'Search Customer: View', 'CUSTOMERS', 'SEARCH_CUSTOMER', 'VIEW', 90),
    ('VENDORS.SEARCH_VENDOR.VIEW', 'Search Vendor: View', 'VENDORS', 'SEARCH_VENDOR', 'VIEW', 615),
    ('HUMAN_RESOURCES.SEARCH_STAFF.VIEW', 'Search Staff: View', 'HUMAN_RESOURCES', 'SEARCH_STAFF', 'VIEW', 675)
ON CONFLICT (code) DO NOTHING;
