-- Down of 0901. Rules made by the three promotion screens become ordinary promotions (PROMOTION) so the old check can be put back; their targets, stores and
-- channels stay, so they keep working, only the screens no longer own them.
UPDATE promo.rules SET created_from = 'PROMOTION' WHERE created_from IN ('SPECIAL_OFFER', 'SALE_ON_STORES', 'SALE_ON_CATEGORIES');

ALTER TABLE promo.rules DROP CONSTRAINT IF EXISTS rules_created_from_check;
ALTER TABLE promo.rules
    ADD CONSTRAINT rules_created_from_check CHECK (created_from IN ('PROMOTION', 'PRODUCT_DISCOUNT'));
