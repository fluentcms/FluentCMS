using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.Sqlite;

public sealed class SqliteJsonDialect : IJsonSqlDialect
{
    private const string BlockTypeField = "type";

    private static readonly MethodInfo Each = Function(nameof(SqliteJsonFunctions.Each));
    private static readonly MethodInfo EachAt = Function(nameof(SqliteJsonFunctions.EachAt));
    private static readonly MethodInfo ExtractText = Function(nameof(SqliteJsonFunctions.ExtractText));
    private static readonly MethodInfo ExtractInteger = Function(nameof(SqliteJsonFunctions.ExtractInteger));
    private static readonly MethodInfo ExtractReal = Function(nameof(SqliteJsonFunctions.ExtractReal));
    private static readonly MethodInfo TypeAt = Function(nameof(SqliteJsonFunctions.TypeAt));
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo AnyEntry = typeof(Queryable).GetMethods()
        .Single(method => method.Name == nameof(Queryable.Any) && method.GetParameters().Length == 2);

    public void ConfigureFunctions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqliteJsonEntry>().HasNoKey().ToTable((string?)null);
        modelBuilder.Entity<SqliteJsonPathEntry>().HasNoKey().ToTable((string?)null);
        modelBuilder.HasDbFunction(Each).HasName("json_each").IsBuiltIn();
        modelBuilder.HasDbFunction(EachAt).HasName("json_each").IsBuiltIn();
        modelBuilder.HasDbFunction(ExtractText).HasName("json_extract").IsBuiltIn();
        modelBuilder.HasDbFunction(ExtractInteger).HasName("json_extract").IsBuiltIn();
        modelBuilder.HasDbFunction(ExtractReal).HasName("json_extract").IsBuiltIn();
        modelBuilder.HasDbFunction(TypeAt).HasName("json_type").IsBuiltIn();
    }

    public Expression HasBlock(Expression json, Expression blockType) =>
        Any<SqliteJsonEntry>(Expression.Call(Each, json), block => BlockTypeIs(block, blockType));

    public Expression AnyBlock(Expression json, Expression blockType, Func<Expression, Expression> predicate) =>
        Any<SqliteJsonEntry>(Expression.Call(Each, json), block => Expression.AndAlso(BlockTypeIs(block, blockType), predicate(block)));

    public Expression ScalarEquals(Expression json, string field, JsonScalarType type, Expression value)
    {
        JsonScalar.CheckValue(type, value);
        var path = Path(field);

        return type switch
        {
            JsonScalarType.Text => Expression.Equal(Expression.Call(ExtractText, json, path), value),
            JsonScalarType.WholeNumber => Expression.Equal(Expression.Call(ExtractInteger, json, path), Expression.Convert(value, typeof(long?))),

            // A JSON number is INTEGER or REAL, while EF binds a decimal as text, which never equals a number.
            JsonScalarType.DecimalNumber => Expression.Equal(Expression.Call(ExtractReal, json, path), Expression.Convert(value, typeof(double?))),

            // json_extract gives 1 for true, which would also equal the number 1.
            JsonScalarType.Boolean => Expression.Equal(
                Expression.Call(TypeAt, json, path),
                Expression.Condition(value, Expression.Constant("true"), Expression.Constant("false"))),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public Expression ScalarContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);
        var path = Path(field);

        return Expression.AndAlso(
            IsOfType(json, path, "text"),
            Expression.Call(Expression.Call(ExtractText, json, path), StringContains, value));
    }

    public Expression ArrayContains(Expression json, string field, Expression value)
    {
        JsonScalar.CheckValue(JsonScalarType.Text, value);
        var path = Path(field);

        return Expression.AndAlso(
            IsOfType(json, path, "array"),
            Any<SqliteJsonPathEntry>(Expression.Call(EachAt, json, path), element => Expression.Equal(element, value)));
    }

    private BinaryExpression BlockTypeIs(Expression block, Expression blockType) =>
        (BinaryExpression)ScalarEquals(block, BlockTypeField, JsonScalarType.Text, blockType);

    private static BinaryExpression IsOfType(Expression json, ConstantExpression path, string type) =>
        Expression.Equal(Expression.Call(TypeAt, json, path), Expression.Constant(type));

    private static MethodCallExpression Any<TEntry>(Expression entries, Func<Expression, Expression> predicate)
    {
        var entry = Expression.Parameter(typeof(TEntry), "entry");
        var body = predicate(Expression.Property(entry, nameof(SqliteJsonEntry.Value)));
        return Expression.Call(AnyEntry.MakeGenericMethod(typeof(TEntry)), entries, Expression.Quote(Expression.Lambda<Func<TEntry, bool>>(body, entry)));
    }

    private static ConstantExpression Path(string field) => Expression.Constant($"$.{JsonScalar.CheckField(field)}");

    private static MethodInfo Function(string name) => typeof(SqliteJsonFunctions).GetMethod(name)!;
}
