using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Data.SqlServer;

// JSON_VALUE returns every scalar as text, so a number would equal the text '12'. OPENJSON exposes the JSON type of
// each key instead, and works on nvarchar(max) and on the native json type alike. Text compares under a binary
// collation, because the other providers are case-sensitive and a database's default usually is not.
// Not yet run on a database (#2711).
public sealed class SqlServerJsonDialect : IJsonSqlDialect
{
    private const string BlockTypeField = "type";
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    // The values of the type column of OPENJSON.
    private const int JsonString = 1;
    private const int JsonNumber = 2;
    private const int JsonBoolean = 3;
    private const int JsonArray = 4;

    private const string DecimalStoreType = "decimal(38,18)";

    private static readonly MethodInfo OpenJson = Function(nameof(SqlServerJsonFunctions.OpenJson));
    private static readonly MethodInfo TryBigint = Function(nameof(SqlServerJsonFunctions.TryBigint));
    private static readonly MethodInfo TryDecimal = Function(nameof(SqlServerJsonFunctions.TryDecimal));
    private static readonly MethodInfo Collate = typeof(RelationalDbFunctionsExtensions).GetMethod(nameof(RelationalDbFunctionsExtensions.Collate))!.MakeGenericMethod(typeof(string));
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo AnyEntry = typeof(Queryable).GetMethods()
        .Single(method => method.Name == nameof(Queryable.Any) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(SqlServerJsonEntry));

    public void ConfigureFunctions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerJsonEntry>().HasNoKey().ToTable((string?)null);
        modelBuilder.HasDbFunction(OpenJson).HasName("OPENJSON").IsBuiltIn();

        // TRY_CONVERT takes a type keyword as its first argument, which a plain mapping cannot express.
        modelBuilder.HasDbFunction(TryBigint).HasName("TRY_CONVERT").IsBuiltIn()
            .HasTranslation(arguments => TryConvert("bigint", arguments[0], typeof(long), new LongTypeMapping("bigint")));
        modelBuilder.HasDbFunction(TryDecimal).HasName("TRY_CONVERT").IsBuiltIn()
            .HasTranslation(arguments => TryConvert(DecimalStoreType, arguments[0], typeof(decimal), new DecimalTypeMapping(DecimalStoreType)));
    }

    public Expression HasBlock(Expression json, Expression blockType) =>
        Any(Expression.Call(OpenJson, json), block => BlockTypeIs(Expression.Property(block, nameof(SqlServerJsonEntry.Value)), blockType));

    public Expression AnyBlock(Expression json, Expression blockType, Func<Expression, Expression> predicate) =>
        Any(Expression.Call(OpenJson, json), entry =>
        {
            var block = Expression.Property(entry, nameof(SqlServerJsonEntry.Value));
            return Expression.AndAlso(BlockTypeIs(block, blockType), predicate(block));
        });

    public Expression ScalarEquals(Expression json, string field, JsonScalarType type, Expression value)
    {
        JsonScalar.CheckValue(type, value);

        return AtKey(json, field, type switch
        {
            JsonScalarType.Text => JsonString,
            JsonScalarType.Boolean => JsonBoolean,
            _ => JsonNumber,
        }, text => type switch
        {
            JsonScalarType.Text => Expression.Equal(Binary(text), value),
            JsonScalarType.WholeNumber => Expression.Equal(Expression.Call(TryBigint, text), Expression.Convert(value, typeof(long?))),
            JsonScalarType.DecimalNumber => Expression.Equal(Expression.Call(TryDecimal, text), Expression.Convert(value, typeof(decimal?))),
            JsonScalarType.Boolean => Expression.Equal(text, Expression.Condition(value, Expression.Constant("true"), Expression.Constant("false"))),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        });
    }

    public Expression ScalarContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);

        return AtKey(json, field, JsonString, text => Expression.Call(Binary(text), StringContains, value));
    }

    public Expression ArrayContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);

        return AtKey(json, field, JsonArray, array => Any(Expression.Call(OpenJson, array), element => Expression.AndAlso(
            Expression.Equal(Expression.Property(element, nameof(SqlServerJsonEntry.Type)), Expression.Constant(JsonString)),
            Expression.Equal(Binary(Expression.Property(element, nameof(SqlServerJsonEntry.Value))), value))));
    }

    public Expression ListContains(Expression json, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);

        return Any(Expression.Call(OpenJson, json), element => Expression.AndAlso(
            Expression.Equal(Expression.Property(element, nameof(SqlServerJsonEntry.Type)), Expression.Constant(JsonString)),
            Expression.Equal(Binary(Expression.Property(element, nameof(SqlServerJsonEntry.Value))), value)));
    }

    private Expression BlockTypeIs(Expression block, Expression blockType) =>
        ScalarEquals(block, BlockTypeField, JsonScalarType.Text, blockType);

    // A key of the object holds this JSON type and its value, as OPENJSON gives it, satisfies the predicate.
    private static MethodCallExpression AtKey(Expression json, string field, int jsonType, Func<Expression, Expression> predicate) =>
        Any(Expression.Call(OpenJson, json), entry => Expression.AndAlso(
            Expression.AndAlso(
                Expression.Equal(Expression.Property(entry, nameof(SqlServerJsonEntry.Key)), Expression.Constant(JsonScalar.CheckField(field))),
                Expression.Equal(Expression.Property(entry, nameof(SqlServerJsonEntry.Type)), Expression.Constant(jsonType))),
            predicate(Expression.Property(entry, nameof(SqlServerJsonEntry.Value)))));

    private static MethodCallExpression Any(Expression entries, Func<ParameterExpression, Expression> predicate)
    {
        var entry = Expression.Parameter(typeof(SqlServerJsonEntry), "entry");
        return Expression.Call(AnyEntry, entries, Expression.Quote(Expression.Lambda<Func<SqlServerJsonEntry, bool>>(predicate(entry), entry)));
    }

    private static MethodCallExpression Binary(Expression text) =>
        Expression.Call(Collate, Expression.Property(null, typeof(EF), nameof(EF.Functions)), text, Expression.Constant(BinaryCollation));

    private static SqlFunctionExpression TryConvert(string storeType, SqlExpression value, Type type, RelationalTypeMapping typeMapping) => new(
        "TRY_CONVERT",
        [new SqlFragmentExpression(storeType), value],
        nullable: true,
        argumentsPropagateNullability: [false, true],
        type,
        typeMapping);

    private static MethodInfo Function(string name) => typeof(SqlServerJsonFunctions).GetMethod(name)!;
}
