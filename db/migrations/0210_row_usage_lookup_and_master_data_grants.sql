-- 0210: master data screens (Phase 0 rows ORG-2.4 to ORG-2.11, ORG-2.17).
--   * core.find_row_usages: "Delete (blocked if the value is used anywhere, show where)" (docx 2.4). Looks at every foreign key that
--     points at the row's table and reports which tables still use the row, so the rule holds for tables that are added later
--     (products, variants, stores ...) without changing code. Runs with the caller's rights, so row-level security applies.
--   * master.sizes may be deleted: sizes are reference data of a size group, not ledger history.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

CREATE FUNCTION core.find_row_usages(p_company uuid, p_table regclass, p_id uuid)
RETURNS TABLE (referencing_table text, usage_count bigint)
LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_foreign_key record;
    v_column_name text;
    v_has_deleted_flag boolean;
    v_sql         text;
    v_count       bigint;
BEGIN
    FOR v_foreign_key IN
        SELECT c.conrelid, c.conkey, c.confkey
          FROM pg_constraint c
         WHERE c.contype = 'f' AND c.confrelid = p_table AND c.conparentid = 0
    LOOP
        -- the referencing column that points at the "id" column of the referenced table (the other one is company_id)
        SELECT a.attname INTO v_column_name
          FROM unnest(v_foreign_key.conkey, v_foreign_key.confkey) AS pair(referencing_number, referenced_number)
          JOIN pg_attribute a ON a.attrelid = v_foreign_key.conrelid AND a.attnum = pair.referencing_number
          JOIN pg_attribute r ON r.attrelid = p_table AND r.attnum = pair.referenced_number
         WHERE r.attname = 'id';
        CONTINUE WHEN v_column_name IS NULL;

        SELECT EXISTS (SELECT 1 FROM pg_attribute a
                        WHERE a.attrelid = v_foreign_key.conrelid AND a.attname = 'is_deleted' AND NOT a.attisdropped)
          INTO v_has_deleted_flag;

        v_sql := format('SELECT count(*) FROM %s WHERE company_id = $1 AND %I = $2%s',
                        v_foreign_key.conrelid::regclass, v_column_name,
                        CASE WHEN v_has_deleted_flag THEN ' AND NOT is_deleted' ELSE '' END);
        EXECUTE v_sql INTO v_count USING p_company, p_id;

        IF v_count > 0 THEN
            referencing_table := v_foreign_key.conrelid::regclass::text;
            usage_count := v_count;
            RETURN NEXT;
        END IF;
    END LOOP;
END $$;
GRANT EXECUTE ON FUNCTION core.find_row_usages(uuid, regclass, uuid) TO pos_app;

GRANT DELETE ON master.sizes TO pos_app;
