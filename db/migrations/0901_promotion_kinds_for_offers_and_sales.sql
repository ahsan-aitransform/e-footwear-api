-- 0901: Promotions (docx 2.12 to 2.16, Part 18).
--   * promo.rules.created_from told a discount of the product screens (4.9 to 4.12) from an ordinary promotion. The Special Offers, Sale on Stores and Sale on
--     Categories screens each own the rules they make, so each gets its own value; the screens list and change only their own kind and never touch the rules
--     of another screen. Deals use promo.bundles and need no new column.
-- Nothing else is needed: the engine (promo.rebuild_effective_prices), the targets, stores, channels, bundles and the conflict groups are in the baseline, and the
-- application role already has the rights these screens use (INSERT, UPDATE, and DELETE on the target, store, channel and bundle item tables).
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

DO $$
DECLARE
    v_constraint_name text;
BEGIN
    SELECT c.conname INTO v_constraint_name
      FROM pg_constraint c
     WHERE c.conrelid = 'promo.rules'::regclass AND c.contype = 'c' AND pg_get_constraintdef(c.oid) LIKE '%created_from%';
    IF v_constraint_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE promo.rules DROP CONSTRAINT %I', v_constraint_name);
    END IF;
END $$;

ALTER TABLE promo.rules
    ADD CONSTRAINT rules_created_from_check
    CHECK (created_from IN ('PROMOTION', 'PRODUCT_DISCOUNT', 'SPECIAL_OFFER', 'SALE_ON_STORES', 'SALE_ON_CATEGORIES'));
