using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content.Query;

internal static class QueryExpressions
{
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;
    private static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo QueryableAny = typeof(Queryable).GetMethods()
        .Single(method => method.Name == nameof(Queryable.Any) && method.GetParameters().Length == 2);
    private static readonly MethodInfo EnumerableContains = typeof(Enumerable).GetMethods()
        .Single(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2);

    public static MethodCallExpression Column(Expression row, string column, Type type) =>
        Expression.Call(EfProperty.MakeGenericMethod(type), row, Expression.Constant(column));

    public static MemberExpression DocumentId(Expression row) =>
        Expression.Property(Expression.Property(row, nameof(QueryRow.Document)), nameof(QueryRow.Document.DocumentId));

    // EF turns a member read on a constant into a query parameter; a bare constant would become a SQL literal.
    public static MemberExpression Bind(object? value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(Holder<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), nameof(Holder<object>.Value));
    }

    public static MemberExpression Bind<T>(T value) => Bind(value, typeof(T));

    public static MethodCallExpression InList(Expression item, IReadOnlyList<object?> values)
    {
        var array = Array.CreateInstance(item.Type, values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            array.SetValue(values[index], index);
        }

        return Expression.Call(EnumerableContains.MakeGenericMethod(item.Type), Bind(array, array.GetType()), item);
    }

    // Text has no comparison operators; EF translates a comparison of string.Compare to the plain SQL operator.
    public static BinaryExpression Compare(ExpressionType comparison, Expression left, Expression right) => left.Type == typeof(string)
        ? Expression.MakeBinary(comparison, Expression.Call(StringCompare, left, right), Expression.Constant(0))
        : Expression.MakeBinary(comparison, left, right);

    public static MethodCallExpression Any<T>(IQueryable<T> source, ParameterExpression item, Expression predicate) =>
        Expression.Call(QueryableAny.MakeGenericMethod(typeof(T)), source.Expression, Expression.Quote(Expression.Lambda<Func<T, bool>>(predicate, item)));

    public static bool IsNullable(StoredField field) =>
        field.ClrType == typeof(string) ? !field.Required : Nullable.GetUnderlyingType(field.ClrType!) is not null;

    public sealed class Holder<T>(T value)
    {
        public T Value { get; } = value;
    }
}
