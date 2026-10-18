using System.Text.RegularExpressions;

namespace FluentCMS.Core.Tools;

public static partial class ToolNames
{
    public const int MaxLength = 64;

    public static bool IsName(string? name)
    {
        return name is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(name);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
