namespace FluentCMS.Data.SqlServer;

// The queries of the catalog that InformationSchemaReader runs for what the standard views do not list.
internal static class SqlServerCatalog
{
    public const string CurrentSchema = "SCHEMA_NAME()";

    // The key columns of the indexes that CREATE INDEX made, not those that back a primary key or a unique constraint.
    public const string Indexes = """
        SELECT t.name, i.name, i.is_unique, i.has_filter, c.name
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE t.schema_id = SCHEMA_ID() AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND ic.key_ordinal > 0
        ORDER BY t.name, i.name, ic.key_ordinal
        """;

    // A column that a check constraint reads cannot be dropped or altered before the constraint is, such as the ISJSON
    // check of a JSON column.
    public const string CheckConstraints = $"""
        SELECT u.TABLE_NAME, u.CONSTRAINT_NAME, u.COLUMN_NAME
        FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE u
        JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS c ON c.CONSTRAINT_SCHEMA = u.CONSTRAINT_SCHEMA AND c.CONSTRAINT_NAME = u.CONSTRAINT_NAME
        WHERE c.CONSTRAINT_TYPE = 'CHECK' AND u.TABLE_SCHEMA = {CurrentSchema}
        """;
}
