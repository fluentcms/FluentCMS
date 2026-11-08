using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.Tests.Query;

// What the query compiler of #2692 hands to IJsonSqlDialect: columns of a property bag and values on a holder.
internal static class RowFilter
{
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;

    public static Expression<Func<Dictionary<string, object>, bool>> Where(Func<ParameterExpression, Expression> filter)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object>), "row");
        return Expression.Lambda<Func<Dictionary<string, object>, bool>>(filter(row), row);
    }

    public static MethodCallExpression Column<T>(ParameterExpression row, string column) =>
        Expression.Call(EfProperty.MakeGenericMethod(typeof(T)), row, Expression.Constant(column));

    public static MemberExpression Bind<T>(T value) =>
        Expression.Property(Expression.Constant(new Holder<T>(value)), nameof(Holder<T>.Value));

    private sealed record Holder<T>(T Value);
}
