namespace FluentCMS.Data.PostgreSql;

// The queries of the catalog that InformationSchemaReader runs for what the standard views do not list.
internal static class PostgreSqlCatalog
{
    public const string CurrentSchema = "current_schema()";

    // The key columns, not the INCLUDE ones. An index that backs a constraint, such as the primary key, is not one that
    // CREATE INDEX made. A key that is an expression has no column (attnum 0) and is listed with an empty name, so that an
    // index of expressions alone, such as a GIN index on to_tsvector, is listed too (#3530).
    public const string Indexes = $"""
        SELECT t.relname, i.relname, x.indisunique, x.indpred IS NOT NULL, COALESCE(a.attname, '')
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        CROSS JOIN LATERAL unnest(x.indkey::smallint[]) WITH ORDINALITY AS k(attnum, position)
        LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
        WHERE n.nspname = {CurrentSchema} AND k.position <= x.indnkeyatts
          AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = x.indexrelid)
        ORDER BY t.relname, i.relname, k.position
        """;
}
