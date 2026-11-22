-- Rolls back 0300. Barcode history is dropped (its rows are lost), discount rules created by the product screens keep their
-- rows but lose the marker column, the wrappers and the audit triggers are removed, and the two extra DELETE grants are revoked.
REVOKE DELETE ON catalog.product_images, catalog.store_price_overrides FROM pos_app;

DROP TRIGGER IF EXISTS trg_audit ON catalog.price_bands;
DROP TRIGGER IF EXISTS trg_audit ON catalog.store_price_overrides;
DROP TRIGGER IF EXISTS trg_audit ON catalog.variants;
DROP TRIGGER IF EXISTS trg_audit ON catalog.products;

DROP FUNCTION IF EXISTS catalog.peek_next_product_code();
DROP FUNCTION IF EXISTS catalog.allocate_next_product_code();
DROP FUNCTION IF EXISTS promo.refresh_effective_prices_for_current_company();

DROP INDEX IF EXISTS promo.ix_rules_created_batch;
DROP INDEX IF EXISTS promo.ix_rules_product_discount;
ALTER TABLE promo.rules DROP COLUMN IF EXISTS created_batch_id, DROP COLUMN IF EXISTS created_from;

DROP TABLE IF EXISTS catalog.barcode_history;
