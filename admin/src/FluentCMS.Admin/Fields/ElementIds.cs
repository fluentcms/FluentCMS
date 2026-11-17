using System.Globalization;

namespace FluentCMS.Admin.Fields;

// Ids of the elements a field's input is labelled and described by, unique in the page.
internal static class ElementIds
{
    private static int _next;

    public static string Next(string prefix) => $"{prefix}-{Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture)}";
}
