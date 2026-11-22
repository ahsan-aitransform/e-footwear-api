-- 0300: Catalogue module (Phase 0 rows ORG-4.1 to ORG-4.15, ADD-09-01 to ADD-09-04).
--   1. catalog.barcode_history: append-only history of every barcode replacement (Part 9.1: "barcode generation and replacement with a complete history").
--   2. promo.rules.created_from: tells a discount created from the product screens (4.9 to 4.12) from an ordinary promotion.
--   3. promo.refresh_effective_prices_for_current_company(): the application role cannot run promo.rebuild_effective_prices (it deletes rows the role has
--      no DELETE right on), and nothing in the database runs it after a price or rule change. This wrapper runs it for the session's company only.
--   4. catalog.allocate_next_product_code() / peek_next_product_code(): company-wide product codes. core.number_series is store-scoped by row-level security,
--      so a store-restricted user could not use the head-office series directly.
--   5. Audit trigger on the catalogue master tables (who changed what, with old and new values) and the two deletes the screens need.
-- The functions run with the owner's rights (SECURITY DEFINER) and take the company from the session, never from a parameter.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

CREATE TABLE catalog.barcode_history (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    variant_id      uuid NOT NULL,
    old_barcode     text NOT NULL CHECK (old_barcode <> ''),
    new_barcode     text NOT NULL CHECK (new_barcode <> ''),
    batch_id        uuid,
    reason          text,
    changed_by      uuid,
    changed_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK (old_barcode <> new_barcode)
);
CREATE INDEX ix_barcode_history_variant ON catalog.barcode_history (company_id, variant_id, changed_at);
CREATE INDEX ix_barcode_history_old     ON catalog.barcode_history (company_id, old_barcode);
CREATE INDEX ix_barcode_history_batch   ON catalog.barcode_history (company_id, batch_id) WHERE batch_id IS NOT NULL;
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON catalog.barcode_history
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
SELECT core.enable_rls('catalog.barcode_history'::regclass, false);
GRANT SELECT, INSERT ON catalog.barcode_history TO pos_app;

ALTER TABLE promo.rules
    ADD COLUMN created_from     text NOT NULL DEFAULT 'PROMOTION' CHECK (created_from IN ('PROMOTION', 'PRODUCT_DISCOUNT')),
    ADD COLUMN created_batch_id uuid;                 -- the client transaction id of the operation that made the rule: a retry finds its own rules
CREATE INDEX ix_rules_product_discount ON promo.rules (company_id, status)
    WHERE created_from = 'PRODUCT_DISCOUNT' AND NOT is_deleted;
CREATE INDEX ix_rules_created_batch ON promo.rules (company_id, created_batch_id) WHERE created_batch_id IS NOT NULL;

CREATE FUNCTION promo.refresh_effective_prices_for_current_company() RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company uuid := core.current_company_id();
BEGIN
    IF v_company IS NULL THEN
        RAISE EXCEPTION 'the session has no company' USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN promo.rebuild_effective_prices(v_company);
END $$;
REVOKE ALL ON FUNCTION promo.refresh_effective_prices_for_current_company() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION promo.refresh_effective_prices_for_current_company() TO pos_app;

CREATE FUNCTION catalog.allocate_next_product_code() RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company    uuid := core.current_company_id();
    v_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_all_stores text := current_setting('app.all_stores', true);
    v_code       text;
BEGIN
    IF v_company IS NULL THEN
        RAISE EXCEPTION 'the session has no company' USING ERRCODE = 'insufficient_privilege';
    END IF;
    -- The head-office series is company-wide; row-level security would hide it from a store-restricted user, so the setting is
    -- widened for this call only and put back before returning (it is transaction-local either way).
    PERFORM set_config('app.all_stores', 'on', true);
    LOOP
        v_code := core.next_number(v_company, v_head_office, 'PRODUCT_CODE')::text;
        EXIT WHEN NOT EXISTS (SELECT 1 FROM catalog.products p WHERE p.company_id = v_company AND p.code = v_code AND NOT p.is_deleted);
    END LOOP;
    PERFORM set_config('app.all_stores', COALESCE(v_all_stores, ''), true);
    RETURN v_code;
END $$;
REVOKE ALL ON FUNCTION catalog.allocate_next_product_code() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION catalog.allocate_next_product_code() TO pos_app;

CREATE FUNCTION catalog.peek_next_product_code() RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company     uuid := core.current_company_id();
    v_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_value       bigint;
BEGIN
    IF v_company IS NULL THEN
        RAISE EXCEPTION 'the session has no company' USING ERRCODE = 'insufficient_privilege';
    END IF;
    SELECT next_value INTO v_value FROM core.number_series
     WHERE company_id = v_company AND store_id = v_head_office AND series_code = 'PRODUCT_CODE' AND period_key = DATE '1900-01-01';
    v_value := COALESCE(v_value, 1);
    WHILE EXISTS (SELECT 1 FROM catalog.products p WHERE p.company_id = v_company AND p.code = v_value::text AND NOT p.is_deleted) LOOP
        v_value := v_value + 1;
    END LOOP;
    RETURN v_value::text;
END $$;
REVOKE ALL ON FUNCTION catalog.peek_next_product_code() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION catalog.peek_next_product_code() TO pos_app;

-- Who changed a product, a size or a price band, with old and new values (the activity log screen reads audit.activity_log).
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON catalog.products
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON catalog.variants
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON catalog.store_price_overrides
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON catalog.price_bands
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();

-- A product picture slot can be emptied, and a store price can go back to the company list price: neither is ledger history.
GRANT DELETE ON catalog.product_images, catalog.store_price_overrides TO pos_app;
