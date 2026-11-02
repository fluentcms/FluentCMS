using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Data.PostgreSql;

// Equality is jsonb containment (@>): it keeps the JSON type, so a number never equals the text '12', does not
// throw on an unexpected shape, and is what a GIN index on the column serves. Not yet run on a database (#2711).
public sealed class PostgreSqlJsonDialect : IJsonSqlDialect
{
    private const string BlockTypeField = "type";
    private const string Jsonb = "jsonb";

    private static readonly MethodInfo Elements = Function(nameof(PostgreSqlJsonFunctions.Elements));
    private static readonly MethodInfo Extract = Function(nameof(PostgreSqlJsonFunctions.Extract));
    private static readonly MethodInfo ExtractText = Function(nameof(PostgreSqlJsonFunctions.ExtractText));
    private static readonly MethodInfo TypeOf = Function(nameof(PostgreSqlJsonFunctions.TypeOf));
    private static readonly MethodInfo ObjectOfText = Function(nameof(PostgreSqlJsonFunctions.ObjectOfText));
    private static readonly MethodInfo ObjectOfBigint = Function(nameof(PostgreSqlJsonFunctions.ObjectOfBigint));
    private static readonly MethodInfo ObjectOfNumeric = Function(nameof(PostgreSqlJsonFunctions.ObjectOfNumeric));
    private static readonly MethodInfo ObjectOfBoolean = Function(nameof(PostgreSqlJsonFunctions.ObjectOfBoolean));
    private static readonly MethodInfo ObjectOfJson = Function(nameof(PostgreSqlJsonFunctions.ObjectOfJson));
    private static readonly MethodInfo ArrayOfText = Function(nameof(PostgreSqlJsonFunctions.ArrayOfText));
    private static readonly MethodInfo ArrayOfJson = Function(nameof(PostgreSqlJsonFunctions.ArrayOfJson));
    private static readonly MethodInfo JsonContains = typeof(NpgsqlJsonDbFunctionsExtensions).GetMethod(nameof(NpgsqlJsonDbFunctionsExtensions.JsonContains))!;
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo AnyElement = typeof(Queryable).GetMethods()
        .Single(method => method.Name == nameof(Queryable.Any) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(PostgreSqlJsonElement));

    public void ConfigureFunctions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgreSqlJsonElement>().HasNoKey().ToTable((string?)null);

        modelBuilder.HasDbFunction(Elements).HasName("jsonb_array_elements").IsBuiltIn().HasParameter("json").HasStoreType(Jsonb);

        Declare(modelBuilder, Extract, "jsonb_extract_path", Jsonb).HasParameter("json").HasStoreType(Jsonb);
        Declare(modelBuilder, ExtractText, "jsonb_extract_path_text", "text").HasParameter("json").HasStoreType(Jsonb);
        Declare(modelBuilder, TypeOf, "jsonb_typeof", "text").HasParameter("json").HasStoreType(Jsonb);

        Declare(modelBuilder, ObjectOfText, "jsonb_build_object", Jsonb).HasParameter("value").HasStoreType("text");
        Declare(modelBuilder, ObjectOfBigint, "jsonb_build_object", Jsonb).HasParameter("value").HasStoreType("bigint");
        Declare(modelBuilder, ObjectOfNumeric, "jsonb_build_object", Jsonb).HasParameter("value").HasStoreType("numeric");
        Declare(modelBuilder, ObjectOfBoolean, "jsonb_build_object", Jsonb).HasParameter("value").HasStoreType("boolean");
        Declare(modelBuilder, ObjectOfJson, "jsonb_build_object", Jsonb).HasParameter("value").HasStoreType(Jsonb);
        Declare(modelBuilder, ArrayOfText, "jsonb_build_array", Jsonb).HasParameter("value").HasStoreType("text");
        Declare(modelBuilder, ArrayOfJson, "jsonb_build_array", Jsonb).HasParameter("value").HasStoreType(Jsonb);
    }

    public Expression HasBlock(Expression json, Expression blockType) =>
        Contains(json, Expression.Call(ArrayOfJson, BlockOfType(blockType)));

    // The outer containment lets a GIN index narrow the rows before the elements are expanded.
    public Expression AnyBlock(Expression json, Expression blockType, Func<Expression, Expression> predicate)
    {
        var element = Expression.Parameter(typeof(PostgreSqlJsonElement), "element");
        var block = Expression.Property(element, nameof(PostgreSqlJsonElement.Value));
        var body = Expression.AndAlso(Contains(block, BlockOfType(blockType)), predicate(block));

        return Expression.AndAlso(
            HasBlock(json, blockType),
            Expression.Call(AnyElement, Expression.Call(Elements, json), Expression.Quote(Expression.Lambda<Func<PostgreSqlJsonElement, bool>>(body, element))));
    }

    public Expression ScalarEquals(Expression json, string field, JsonScalarType type, Expression value)
    {
        JsonScalar.CheckValue(type, value);
        var key = Key(field);

        return Contains(json, type switch
        {
            JsonScalarType.Text => Expression.Call(ObjectOfText, key, value),
            JsonScalarType.WholeNumber => Expression.Call(ObjectOfBigint, key, Expression.Convert(value, typeof(long?))),
            JsonScalarType.DecimalNumber => Expression.Call(ObjectOfNumeric, key, Expression.Convert(value, typeof(decimal?))),
            JsonScalarType.Boolean => Expression.Call(ObjectOfBoolean, key, Expression.Convert(value, typeof(bool?))),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        });
    }

    public Expression ScalarContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);
        var key = Key(field);

        return Expression.AndAlso(
            Expression.Equal(Expression.Call(TypeOf, Expression.Call(Extract, json, key)), Expression.Constant("string")),
            Expression.Call(Expression.Call(ExtractText, json, key), StringContains, value));
    }

    public Expression ArrayContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);

        return Contains(json, Expression.Call(ObjectOfJson, Key(field), Expression.Call(ArrayOfText, value)));
    }

    private static MethodCallExpression BlockOfType(Expression blockType) =>
        Expression.Call(ObjectOfText, Key(BlockTypeField), blockType);

    private static MethodCallExpression Contains(Expression json, Expression contained) => Expression.Call(
        JsonContains,
        Expression.Property(null, typeof(EF), nameof(EF.Functions)),
        Expression.Convert(json, typeof(object)),
        Expression.Convert(contained, typeof(object)));

    private static ConstantExpression Key(string field) => Expression.Constant(JsonScalar.CheckField(field));

    private static DbFunctionBuilder Declare(ModelBuilder modelBuilder, MethodInfo function, string name, string storeType) =>
        modelBuilder.HasDbFunction(function).HasName(name).IsBuiltIn().HasStoreType(storeType);

    private static MethodInfo Function(string name) => typeof(PostgreSqlJsonFunctions).GetMethod(name)!;
}
