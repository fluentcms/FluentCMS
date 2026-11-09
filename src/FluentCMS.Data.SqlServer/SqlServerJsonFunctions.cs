using System.ComponentModel.DataAnnotations.Schema;

namespace FluentCMS.Data.SqlServer;

// A row of OPENJSON without a WITH clause.
public sealed class SqlServerJsonEntry
{
    [Column("key")]
    public string? Key { get; set; }

    [Column("value")]
    public string? Value { get; set; }

    [Column("type")]
    public int Type { get; set; }
}

// EF function mappings; SqlServerJsonDialect declares them on the model. They run only as SQL.
public static class SqlServerJsonFunctions
{
    public static IQueryable<SqlServerJsonEntry> OpenJson(string? json) => throw new NotSupportedException();

    public static decimal? TryDecimal(string? value) => throw new NotSupportedException();
}
