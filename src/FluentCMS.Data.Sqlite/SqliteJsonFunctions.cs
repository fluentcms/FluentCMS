using System.ComponentModel.DataAnnotations.Schema;

namespace FluentCMS.Data.Sqlite;

// A row of json_each.
public sealed class SqliteJsonEntry
{
    [Column("value")]
    public string? Value { get; set; }
}

// A row of json_each with a path. EF cannot map one row type to both forms of the function.
public sealed class SqliteJsonPathEntry
{
    [Column("value")]
    public string? Value { get; set; }

    [Column("type")]
    public string? Type { get; set; }
}

// EF function mappings; SqliteJsonDialect declares them on the model. They run only as SQL.
public static class SqliteJsonFunctions
{
    public static IQueryable<SqliteJsonEntry> Each(string? json) => throw new NotSupportedException();

    public static IQueryable<SqliteJsonPathEntry> EachAt(string? json, string path) => throw new NotSupportedException();

    public static string? ExtractText(string? json, string path) => throw new NotSupportedException();

    public static long? ExtractInteger(string? json, string path) => throw new NotSupportedException();

    public static double? ExtractReal(string? json, string path) => throw new NotSupportedException();

    public static string? TypeAt(string? json, string path) => throw new NotSupportedException();
}
