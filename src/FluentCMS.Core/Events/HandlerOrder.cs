using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Hooks;

namespace FluentCMS.Core.Events;

// [HookOrder], then the plugin dependency order the host supplies as a tiebreaker, then registration order.
internal static class HandlerOrder
{
    private static readonly ConcurrentDictionary<Type, int> Orders = new();

    public static List<T> Sort<T>(IEnumerable<T> handlers, Func<T, Type> typeOf, IHookTiebreaker? tiebreaker) =>
        [.. handlers
            .OrderBy(handler => Of(typeOf(handler)))
            .ThenBy(handler => tiebreaker?.Rank(typeOf(handler)) ?? 0)];

    private static int Of(Type handler) =>
        Orders.GetOrAdd(handler, static type => type.GetCustomAttribute<HookOrderAttribute>()?.Order ?? 0);
}
