using System.Collections;
using FluentCMS.Content;

namespace FluentCMS.Core.Content.Query;

internal static class FilterTree
{
    // Every level of the bracket grammar is at most two nodes, so anything the parser accepts is within this.
    public const int MaxDepth = 64;

    // Stops at the first limit it crosses. Compiling and writing recurse, and a long chain from a front end that is
    // not the parser would otherwise overflow the stack, which ends the process.
    public static bool IsWithin(ContentFilter filter, int maxNodes)
    {
        var nodes = 0;
        return Walk(filter, 1, maxNodes, ref nodes);
    }

    private static bool Walk(ContentFilter filter, int depth, int maxNodes, ref int nodes)
    {
        if (depth > MaxDepth || ++nodes > maxNodes)
        {
            return false;
        }

        switch (filter)
        {
            case And every:
                return WalkAll(every.Filters, depth, maxNodes, ref nodes);
            case Or any:
                return WalkAll(any.Filters, depth, maxNodes, ref nodes);
            case Not negated:
                return Walk(negated.Filter, depth + 1, maxNodes, ref nodes);
            case FieldFilter { Value: IEnumerable items and not string }:
                foreach (var _ in items)
                {
                    if (++nodes > maxNodes)
                    {
                        return false;
                    }
                }

                return true;
            default:
                return true;
        }
    }

    private static bool WalkAll(IReadOnlyList<ContentFilter> filters, int depth, int maxNodes, ref int nodes)
    {
        foreach (var filter in filters)
        {
            if (!Walk(filter, depth + 1, maxNodes, ref nodes))
            {
                return false;
            }
        }

        return true;
    }
}
