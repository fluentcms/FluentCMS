using System.Text.RegularExpressions;

namespace FluentCMS.Tests.Admin;

internal static partial class Utilities
{
    // Physical utilities stay put when the root dir attribute flips; the logical ones (ms, pe, start, end, text-start) follow it.
    public static bool IsPhysical(string className) => Physical().IsMatch(className);

    [GeneratedRegex("(^|:)-?(ml|mr|pl|pr|left|right|rounded-[lrtb][lr]?|border-[lr]|text-left|text-right|float-left|float-right)(-|$)")]
    private static partial Regex Physical();
}
