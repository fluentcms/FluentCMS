namespace FluentCMS.Data.PostgreSql;

// The queries of the catalog that InformationSchemaReader runs for what the standard views do not list.
internal static class PostgreSqlCatalog
{
    public const string CurrentSchema = "current_schema()";

    // An index that backs a constraint, such as the primary key, is not one that CREATE INDEX made.
    public const string Indexes = $"""
        SELECT t.relname, i.relname, x.indisunique, x.indpred IS NOT NULL, a.attname
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        CROSS JOIN LATERAL unnest(x.indkey::smallint[]) WITH ORDINALITY AS k(attnum, position)
        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
        WHERE n.nspname = {CurrentSchema}
          AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = x.indexrelid)
        ORDER BY t.relname, i.relname, k.position
        """;
}
