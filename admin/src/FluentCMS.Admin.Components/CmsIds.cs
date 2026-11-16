namespace FluentCMS.Admin.Components;

internal static class CmsIds
{
    private static int _next;

    public static string Next() => "cms-" + Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
