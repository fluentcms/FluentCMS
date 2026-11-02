using System.ComponentModel.DataAnnotations.Schema;

namespace FluentCMS.Data.PostgreSql;

// A row of jsonb_array_elements.
public sealed class PostgreSqlJsonElement
{
    [Column("value", TypeName = "jsonb")]
    public string? Value { get; set; }
}

// EF function mappings; PostgreSqlJsonDialect declares them on the model. They run only as SQL.
public static class PostgreSqlJsonFunctions
{
    public static IQueryable<PostgreSqlJsonElement> Elements(string? json) => throw new NotSupportedException();

    public static string? Extract(string? json, string key) => throw new NotSupportedException();

    public static string? ExtractText(string? json, string key) => throw new NotSupportedException();

    public static string? TypeOf(string? json) => throw new NotSupportedException();

    public static string ObjectOfText(string key, string? value) => throw new NotSupportedException();

    public static string ObjectOfBigint(string key, long? value) => throw new NotSupportedException();

    public static string ObjectOfNumeric(string key, decimal? value) => throw new NotSupportedException();

    public static string ObjectOfBoolean(string key, bool? value) => throw new NotSupportedException();

    public static string ObjectOfJson(string key, string? value) => throw new NotSupportedException();

    public static string ArrayOfText(string? value) => throw new NotSupportedException();

    public static string ArrayOfJson(string? value) => throw new NotSupportedException();
}
