using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Storage.Spike.Tests;

// The shape of the spec's filter AST, reduced to what the spike has to prove.
internal abstract record Filter;

internal sealed record ColumnEquals(string Column, object Value) : Filter;

internal sealed record StateIs(int State) : Filter;

internal sealed record HasBlock(string Column, string BlockType) : Filter;

internal sealed record BlockFieldEquals(string Column, string BlockType, string Field, string Value) : Filter;

internal sealed record JsonFieldEquals(string Column, string Field, string Value) : Filter;

internal sealed record AndFilter(Filter Left, Filter Right) : Filter;

internal sealed record NotFilter(Filter Inner) : Filter;

internal static class FilterCompiler
{
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;
    private static readonly MethodInfo JsonEach = typeof(SpikeDbContext).GetMethod(nameof(SpikeDbContext.JsonEach))!;
    private static readonly MethodInfo JsonText = typeof(SpikeDbContext).GetMethod(nameof(SpikeDbContext.JsonText))!;
    private static readonly MethodInfo AnyWithPredicate = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.Any) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(JsonEachRow));

    public static Expression<Func<Dictionary<string, object>, bool>> Compile(SpikeDbContext context, Filter filter)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object>), "row");
        return Expression.Lambda<Func<Dictionary<string, object>, bool>>(Build(context, filter, row), row);
    }

    private static Expression Build(SpikeDbContext context, Filter filter, ParameterExpression row) => filter switch
    {
        // State is inlined to match the partial index predicate as written; every user value stays a parameter.
        StateIs state => Expression.Equal(Column(row, "state", typeof(int)), Expression.Constant(state.State)),
        ColumnEquals column => Expression.Equal(Column(row, column.Column, column.Value.GetType()), Parameter(column.Value)),
        JsonFieldEquals json => Expression.Equal(
            Expression.Call(JsonText, Column(row, json.Column, typeof(string)), Expression.Constant(Path(json.Field))),
            Parameter(json.Value)),
        HasBlock block => AnyBlock(context, row, block.Column, entry => BlockTypeIs(entry, block.BlockType)),
        BlockFieldEquals block => AnyBlock(context, row, block.Column, entry => Expression.AndAlso(
            BlockTypeIs(entry, block.BlockType),
            Expression.Equal(BlockField(entry, block.Field), Parameter(block.Value)))),
        AndFilter and => Expression.AndAlso(Build(context, and.Left, row), Build(context, and.Right, row)),
        NotFilter not => Expression.Not(Build(context, not.Inner, row)),
        _ => throw new NotSupportedException(filter.GetType().Name),
    };

    private static MethodCallExpression AnyBlock(SpikeDbContext context, ParameterExpression row, string column, Func<ParameterExpression, Expression> predicate)
    {
        var entry = Expression.Parameter(typeof(JsonEachRow), "entry");
        return Expression.Call(
            AnyWithPredicate,
            Expression.Call(Expression.Constant(context), JsonEach, Column(row, column, typeof(string))),
            Expression.Quote(Expression.Lambda<Func<JsonEachRow, bool>>(predicate(entry), entry)));
    }

    private static BinaryExpression BlockTypeIs(ParameterExpression entry, string blockType) =>
        Expression.Equal(BlockField(entry, "type"), Parameter(blockType));

    private static MethodCallExpression BlockField(ParameterExpression entry, string field) =>
        Expression.Call(JsonText, Expression.Property(entry, nameof(JsonEachRow.Value)), Expression.Constant(Path(field)));

    private static MethodCallExpression Column(ParameterExpression row, string column, Type type) =>
        Expression.Call(EfProperty.MakeGenericMethod(type), row, Expression.Constant(column));

    // EF turns a member read on a constant into a query parameter; a bare constant would become a SQL literal.
    private static MemberExpression Parameter(object value)
    {
        var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(value.GetType()), value)!;
        return Expression.Property(Expression.Constant(holder), "Value");
    }

    // Q7 caps paths at one level, and only schema field names reach this point.
    private static string Path(string field) =>
        field.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? $"$.{field}"
            : throw new ArgumentException($"'{field}' is not a one-level JSON path.", nameof(field));

    private sealed record ValueHolder<T>(T Value);
}
