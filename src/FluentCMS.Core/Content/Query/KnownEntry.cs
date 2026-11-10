using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content.Query;

// What a write has already read or written of the one entry a read by id asks for, in the read's state and locale, so
// that the read does not ask the database for it again. Row is the entry's rows as the database holds them; References
// are its references in that state and locale, in position order. Readable is what the reads given this have found
// readable, which holds for every read in the same scope. The engine takes the row only where its query has nothing to
// filter it by but the id: no row rule and no search.
internal sealed class KnownEntry
{
    public QueryRow? Row { get; set; }

    public IReadOnlyList<Reference>? References { get; set; }

    public ReadableTargets Readable { get; } = new();
}

// What reads have found readable of each target type. The answer for a type depends on the scope of the read, which every
// level of a read and every read given the same KnownEntry share, and not on the other ids asked with it; null is every
// target of the type.
internal sealed class ReadableTargets
{
    private readonly Dictionary<SchemaType, (HashSet<Guid> Asked, HashSet<Guid>? Readable)> _byType = [];

    public bool TryAnswer(SchemaType target, List<Guid> ids, out HashSet<Guid>? readable, out List<Guid> unknown)
    {
        if (!_byType.TryGetValue(target, out var known))
        {
            readable = null;
            unknown = ids;
            return false;
        }

        readable = known.Readable;
        unknown = known.Readable is null ? [] : [.. ids.Where(id => !known.Asked.Contains(id))];
        return unknown.Count == 0;
    }

    public void Forget(SchemaType target) => _byType.Remove(target);

    public HashSet<Guid>? Add(SchemaType target, List<Guid> ids, HashSet<Guid>? readable)
    {
        if (!_byType.TryGetValue(target, out var known))
        {
            _byType[target] = ([.. ids], readable);
            return readable;
        }

        known.Asked.UnionWith(ids);
        if (readable is null || known.Readable is null)
        {
            // A type the scope does not limit answers null whatever the ids, so the two cannot differ.
            _byType[target] = (known.Asked, null);
            return null;
        }

        known.Readable.UnionWith(readable);
        return known.Readable;
    }
}

// Runs a plan's projection on a row in memory, as the database would: EF.Property reads the column from the property bag.
// A projection that calls anything else EF translates cannot run here, and null says so.
internal static class InMemoryProjection
{
    private static readonly MethodInfo Column = typeof(InMemoryProjection).GetMethod(nameof(ColumnOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static object?[]? Run(Expression<Func<QueryRow, object?[]>> projection, QueryRow row)
    {
        var rewriter = new Rewriter();
        var body = rewriter.Visit(projection.Body);
        if (rewriter.Untranslatable)
        {
            return null;
        }

        return Expression.Lambda<Func<QueryRow, object?[]>>(body, projection.Parameters).Compile(preferInterpretation: true)(row);
    }

    private static T ColumnOf<T>(object? owner, string name) =>
        owner is IDictionary<string, object> bag && bag.TryGetValue(name, out var value) && value is not null ? (T)value : default!;

    private sealed class Rewriter : ExpressionVisitor
    {
        public bool Untranslatable { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property) && node.Arguments[1] is ConstantExpression { Value: string name })
            {
                return Expression.Call(Column.MakeGenericMethod(node.Method.GetGenericArguments()[0]), Visit(node.Arguments[0]), Expression.Constant(name));
            }

            if (node.Method.DeclaringType?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
            {
                Untranslatable = true;
            }

            return base.VisitMethodCall(node);
        }
    }
}
