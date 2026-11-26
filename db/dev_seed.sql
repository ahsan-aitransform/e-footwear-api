-- =====================================================================
--  DEV SEED: a demo company with stores, master lists, products with stock, customers, vendors and staff.
--  For LOCAL TESTING ONLY. Never run this against a shared or production database.
--
--  Run once, AFTER the baseline and all migrations, as the schema owner:
--    psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/dev_seed.sql
--
--  Needs a platform administrator named devadmin (it supplies the demo password hash):
--    PLATFORM_ADMIN_PASSWORD='DemoPass123' dotnet run --project E-Footwear-Api -- create-platform-admin devadmin "Dev Admin"
--
--  Safe to run twice: it does nothing when the company "Demo Footwear" already exists.
--  Result: owner login owner@demo.test, password DemoPass123 (the same password as devadmin).
-- =====================================================================
DO $seed$
DECLARE
    v_admin     uuid;
    v_hash      text;
    v_company   uuid;
    v_main      uuid;
    v_lhr       uuid;
    v_owner     uuid;
    v_dept_shoes uuid;
    v_dept_acc  uuid;
    v_group_adult uuid;
    v_group_kids  uuid;
    v_unit      uuid;
    v_loc       uuid;
    v_product   uuid;
    v_variant   uuid;
    v_lines     jsonb;
    v_city_khi  uuid;
    v_city_lhr  uuid;
    v_city_fsd  uuid;
    r           record;
    sz          text;
BEGIN
    SELECT id, password_hash INTO v_admin, v_hash FROM platform.admins WHERE lower(username) = 'devadmin';
    IF v_admin IS NULL THEN
        RAISE EXCEPTION 'create the platform administrator devadmin first (see the header of this file)';
    END IF;
    IF EXISTS (SELECT 1 FROM platform.companies WHERE name = 'Demo Footwear') THEN
        RAISE NOTICE 'Demo Footwear already exists: nothing to do';
        RETURN;
    END IF;

    -- company, head office, first store, chart of accounts and the owner login (same function the platform screen uses)
    SELECT out_company_id, out_store_id, out_owner_id INTO v_company, v_main, v_owner
      FROM platform.create_company(v_admin, 'Demo Footwear', 'Asia/Karachi', 'owner@demo.test', 'Demo Owner', v_hash, 'MAIN', 'Main Store');
    UPDATE sec.users SET must_change_password = false WHERE company_id = v_company AND id = v_owner;

    -- act as the owner for the rest of the script
    PERFORM set_config('app.company_id', v_company::text, true);
    PERFORM set_config('app.user_id', v_owner::text, true);
    PERFORM set_config('app.all_stores', 'on', true);

    INSERT INTO core.stores (company_id, code, name) VALUES (v_company, 'LHR', 'Lahore Store') RETURNING id INTO v_lhr;
    INSERT INTO core.stores (company_id, code, name) VALUES (v_company, 'FSD', 'Faisalabad Store');

    -- ---------------------------------------------------------------- master lists
    INSERT INTO master.departments (company_id, name) VALUES (v_company, 'Shoes'), (v_company, 'Accessories') ON CONFLICT DO NOTHING;
    SELECT id INTO v_dept_shoes FROM master.departments WHERE company_id = v_company AND name = 'Shoes';
    SELECT id INTO v_dept_acc   FROM master.departments WHERE company_id = v_company AND name = 'Accessories';
    INSERT INTO master.categories (company_id, department_id, name) VALUES
        (v_company, v_dept_shoes, 'Casual'), (v_company, v_dept_shoes, 'Formal'), (v_company, v_dept_shoes, 'Sandal'),
        (v_company, v_dept_shoes, 'Sports'), (v_company, v_dept_acc, 'Socks') ON CONFLICT DO NOTHING;
    INSERT INTO master.brands (company_id, name) VALUES (v_company, 'Bata'), (v_company, 'Service'), (v_company, 'Borjan'), (v_company, 'Stylo') ON CONFLICT DO NOTHING;
    INSERT INTO master.seasons (company_id, name) VALUES (v_company, 'Summer'), (v_company, 'Winter'), (v_company, 'All Season') ON CONFLICT DO NOTHING;
    INSERT INTO master.genders (company_id, name) VALUES (v_company, 'Men'), (v_company, 'Women'), (v_company, 'Kids') ON CONFLICT DO NOTHING;
    INSERT INTO master.colors (company_id, name) VALUES (v_company, 'Black'), (v_company, 'Brown'), (v_company, 'White'), (v_company, 'Blue'), (v_company, 'Tan') ON CONFLICT DO NOTHING;
    INSERT INTO master.units (company_id, name) VALUES (v_company, 'Pair') ON CONFLICT DO NOTHING;
    INSERT INTO master.locations (company_id, name) VALUES (v_company, 'Shop') ON CONFLICT DO NOTHING;
    INSERT INTO master.cities (company_id, name) VALUES (v_company, 'Karachi'), (v_company, 'Lahore'), (v_company, 'Faisalabad'), (v_company, 'Multan') ON CONFLICT DO NOTHING;
    SELECT id INTO v_unit FROM master.units WHERE company_id = v_company AND name = 'Pair';
    SELECT id INTO v_loc  FROM master.locations WHERE company_id = v_company AND name = 'Shop';
    SELECT id INTO v_city_khi FROM master.cities WHERE company_id = v_company AND name = 'Karachi';
    SELECT id INTO v_city_lhr FROM master.cities WHERE company_id = v_company AND name = 'Lahore';
    SELECT id INTO v_city_fsd FROM master.cities WHERE company_id = v_company AND name = 'Faisalabad';

    INSERT INTO master.size_groups (company_id, name) VALUES (v_company, 'Adult'), (v_company, 'Kids') ON CONFLICT DO NOTHING;
    SELECT id INTO v_group_adult FROM master.size_groups WHERE company_id = v_company AND name = 'Adult';
    SELECT id INTO v_group_kids  FROM master.size_groups WHERE company_id = v_company AND name = 'Kids';
    INSERT INTO master.sizes (company_id, size_group_id, size_code, sort_order)
    SELECT v_company, v_group_adult, s::text, s FROM generate_series(5, 12) s ON CONFLICT DO NOTHING;
    INSERT INTO master.sizes (company_id, size_group_id, size_code, sort_order)
    SELECT v_company, v_group_kids, s::text, s FROM generate_series(1, 4) s ON CONFLICT DO NOTHING;

    -- price bands (they do not overlap)
    INSERT INTO catalog.price_bands (company_id, name, min_price, max_price, sort_order) VALUES
        (v_company, 'Economy', 0, 1999, 1), (v_company, 'Standard', 2000, 3999, 2), (v_company, 'Premium', 4000, NULL, 3);

    -- ---------------------------------------------------------------- products: article, category, brand, season, gender, color, buy, sell, sizes
    FOR r IN
        SELECT * FROM (VALUES
            ('ART-1001', 'Classic Leather Loafer',  'Formal', 'Bata',    'All Season', 'Men',   'Black', 2600, 3999, 'Adult', '7,8,9,10,11'),
            ('ART-1002', 'Oxford Derby',            'Formal', 'Service', 'Winter',     'Men',   'Brown', 3100, 4799, 'Adult', '7,8,9,10,11'),
            ('ART-1003', 'Daily Casual Sneaker',    'Casual', 'Borjan',  'All Season', 'Men',   'White', 1800, 2799, 'Adult', '7,8,9,10,11'),
            ('ART-1004', 'Canvas Slip-on',          'Casual', 'Stylo',   'Summer',     'Men',   'Blue',  900,  1499, 'Adult', '6,7,8,9,10'),
            ('ART-1005', 'Running Shoe Pro',        'Sports', 'Service', 'All Season', 'Men',   'Black', 2400, 3499, 'Adult', '7,8,9,10,11'),
            ('ART-1006', 'Ladies Block Heel',       'Formal', 'Stylo',   'Summer',     'Women', 'Tan',   1700, 2599, 'Adult', '5,6,7,8'),
            ('ART-1007', 'Ladies Flat Sandal',      'Sandal', 'Bata',    'Summer',     'Women', 'Brown', 700,  1199, 'Adult', '5,6,7,8'),
            ('ART-1008', 'Gents Open Sandal',       'Sandal', 'Borjan',  'Summer',     'Men',   'Brown', 800,  1299, 'Adult', '7,8,9,10,11'),
            ('ART-1009', 'Kids School Shoe',        'Formal', 'Bata',    'All Season', 'Kids',  'Black', 1200, 1899, 'Kids',  '1,2,3,4'),
            ('ART-1010', 'Cotton Socks (pack)',     'Socks',  'Stylo',   'All Season', 'Men',   'White', 150,  299,  'Adult', '9')
        ) AS t(code, name, category, brand, season, gender, color, buy, sell, grp, sizes)
    LOOP
        INSERT INTO catalog.products (company_id, code, name, department_id, category_id, brand_id, season_id, gender_id, unit_id, location_id)
        SELECT v_company, r.code, r.name,
               c.department_id, c.id,
               (SELECT id FROM master.brands  WHERE company_id = v_company AND name = r.brand),
               (SELECT id FROM master.seasons WHERE company_id = v_company AND name = r.season),
               (SELECT id FROM master.genders WHERE company_id = v_company AND name = r.gender),
               v_unit, v_loc
          FROM master.categories c WHERE c.company_id = v_company AND c.name = r.category
        RETURNING id INTO v_product;

        FOREACH sz IN ARRAY string_to_array(r.sizes, ',') LOOP
            INSERT INTO catalog.variants (company_id, product_id, color_id, size_group_id, size_code, barcode)
            VALUES (v_company, v_product,
                    (SELECT id FROM master.colors WHERE company_id = v_company AND name = r.color),
                    CASE WHEN r.grp = 'Kids' THEN v_group_kids ELSE v_group_adult END,
                    sz, r.code || '-' || sz)
            RETURNING id INTO v_variant;
            INSERT INTO catalog.variant_prices (company_id, variant_id, purchase_price, sale_price)
            VALUES (v_company, v_variant, r.buy, r.sell);
        END LOOP;
    END LOOP;

    -- ---------------------------------------------------------------- opening stock through the real adjustment function (keeps ledger and accounts right)
    SELECT jsonb_agg(jsonb_build_object('variantId', v.id, 'quantity', 12, 'reason', 'Opening stock (seed)'))
      INTO v_lines FROM catalog.variants v WHERE v.company_id = v_company;
    PERFORM inv.post_stock_adjustment(v_company, v_main, CURRENT_DATE, gen_random_uuid(), 'Opening stock (seed)', v_lines);

    SELECT jsonb_agg(jsonb_build_object('variantId', v.id, 'quantity', 6, 'reason', 'Opening stock (seed)'))
      INTO v_lines FROM catalog.variants v WHERE v.company_id = v_company;
    PERFORM inv.post_stock_adjustment(v_company, v_lhr, CURRENT_DATE, gen_random_uuid(), 'Opening stock (seed)', v_lines);

    -- ---------------------------------------------------------------- customers: name and phone required; email, birth date and city optional
    INSERT INTO contacts.customers (company_id, code, name, phone, email, birth_date, city_id) VALUES
        (v_company, contacts.next_code(v_company), 'Junaid Aslam',  '0300-1234567', 'junaid@example.com', DATE '1992-04-18', v_city_khi),
        (v_company, contacts.next_code(v_company), 'Ayesha Khan',   '0321-7654321', 'ayesha@example.com', DATE '1995-11-02', v_city_lhr),
        (v_company, contacts.next_code(v_company), 'Bilal Ahmed',   '0333-1112233', NULL,                 NULL,              v_city_fsd),
        (v_company, contacts.next_code(v_company), 'Sana Malik',    '0345-9988776', 'sana@example.com',   NULL,              NULL),
        (v_company, contacts.next_code(v_company), 'Usman Raza',    '0301-5556677', NULL,                 NULL,              NULL);

    -- ---------------------------------------------------------------- vendors (credit limit is vendor-only)
    INSERT INTO contacts.vendors (company_id, code, name, phone, email, city_id, credit_limit) VALUES
        (v_company, contacts.next_code(v_company), 'Karachi Footwear Traders', '021-3456789',  'sales@kft.example.com', v_city_khi, 2000000),
        (v_company, contacts.next_code(v_company), 'Lahore Leather House',     '042-3567890',  NULL,                    v_city_lhr, 1500000),
        (v_company, contacts.next_code(v_company), 'Faisalabad Hosiery Mills', '041-2345678',  NULL,                    v_city_fsd, NULL);

    -- ---------------------------------------------------------------- staff
    INSERT INTO contacts.staff (company_id, code, name, phone, designation, is_manager, base_salary, current_store_id, joined_on) VALUES
        (v_company, contacts.next_code(v_company), 'Kamran Ali',  '0300-1010101', 'Store Manager', true,  60000, v_main, DATE '2025-01-01'),
        (v_company, contacts.next_code(v_company), 'Hina Tariq',  '0301-2020202', 'Salesman',      false, 35000, v_main, DATE '2025-03-01'),
        (v_company, contacts.next_code(v_company), 'Rizwan Shah', '0302-3030303', 'Salesman',      false, 35000, v_lhr,  DATE '2025-06-01');

    RAISE NOTICE 'Demo Footwear created: company %, owner login owner@demo.test / DemoPass123', v_company;
END
$seed$;
