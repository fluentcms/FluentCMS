using System.Runtime.CompilerServices;
using FluentCMS.Content;

namespace FluentCMS.Core.Content.Query;

// The query parameter a condition was parsed from, so that an HTTP caller's errors name filter[or][1][title][eq] and
// not title, which two conditions may share. Only a parse that asks for it records one; the compiler reports on it
// when it is there, for the condition and every condition it derives from it.
internal static class FilterSources
{
    private static readonly ConditionalWeakTable<FieldFilter, string> Keys = new();

    public static void Remember(FieldFilter filter, string key) => Keys.AddOrUpdate(filter, key);

    public static string? Of(FieldFilter filter) => Keys.TryGetValue(filter, out var key) ? key : null;
}
