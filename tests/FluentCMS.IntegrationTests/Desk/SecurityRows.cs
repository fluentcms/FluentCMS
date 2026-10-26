using System.Text.RegularExpressions;

namespace FluentCMS.IntegrationTests.Desk;

// The rows of the security view as a person reads them: each event's operation, the kind of who did it and their name. Tests that
// append a SEC-08 event through the code that owns it read it back here, so a renamed operation drops off the view and fails them.
internal static partial class SecurityRows
{
    public static List<(string Operation, string Kind, string Name)> Parse(string page)
    {
        return [.. Row().Matches(page).Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value))];
    }

    [GeneratedRegex("data-event=\"([a-z_.]+)\".*?data-initiator=\"([a-z]+)\".*?<bdi>([^<]*)</bdi>", RegexOptions.Singleline)]
    private static partial Regex Row();
}
