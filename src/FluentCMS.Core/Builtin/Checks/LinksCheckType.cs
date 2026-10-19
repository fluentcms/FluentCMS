using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Every link in the text is one a reader can follow (CHK-01): an absolute http, https, mailto or tel URL, or a path or fragment on
// the site. Links are found in Markdown (inline, reference definitions and autolinks), in HTML href and src attributes, and as
// bare URLs, and a script URL is found anywhere. 'hosts' limits web links to those hosts and their subdomains, and 'httpsOnly'
// refuses plain http. Whether a page answers is not asked: that needs outbound requests, which the administrator allow-lists
// (SEC-05).
internal sealed partial class LinksCheckType : ICheckType
{
    private const RegexOptions Linear = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly string[] Schemes = ["http", "https", "mailto", "tel"];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // A destination may hold one level of balanced parentheses, as CommonMark allows, or anything inside angle brackets.
    private static readonly Regex MarkdownLink = new(
        @"\[(?:[^\[\]]|\[[^\[\]]*\])*\]\(\s*(?:<([^<>\r\n]*)>|((?:[^\s<>()]|\([^\s<>()]*\))*))(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)",
        Linear,
        MatchTimeout);

    private static readonly Regex MarkdownDefinition = new(
        @"^[ ]{0,3}\[[^\]\r\n]+\]:[ \t]*(?:<([^<>\r\n]*)>|([^\s<>]+))",
        Linear | RegexOptions.Multiline,
        MatchTimeout);

    private static readonly Regex HtmlLink = new(@"\b(?:href|src)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", Linear, MatchTimeout);

    private static readonly Regex AutoLink = new(@"<([a-z][a-z0-9+.\-]{1,31}:[^\s<>]*)>", Linear, MatchTimeout);

    private static readonly Regex ScriptUrl = new(@"(?:java|vb)script:", Linear, MatchTimeout);

    public string Name => "links";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var hosts = reader.OptionalStringList("hosts");
        reader.Flag("httpsOnly");

        if (hosts?.FirstOrDefault(host => AsciiHost(host) is null) is { } invalid)
        {
            reader.Invalid("hosts", $"must hold host names such as 'example.com', and '{invalid}' is not one");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var reader = new CheckSettingsReader(Name, input.Settings);
        var hosts = reader.OptionalStringList("hosts")?.Select(AsciiHost).OfType<string>().ToList();
        var httpsOnly = reader.Flag("httpsOnly");
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject))
        {
            var field = CheckContent.FieldOf(pointer);
            foreach (var link in Links(text))
            {
                if (Problem(link, hosts, httpsOnly) is { } problem)
                {
                    findings.Add(new CheckFinding($"Fix the link '{CheckContent.Quote(link)}' in {field}: {problem}", pointer, link));
                }
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static string? Problem(string found, IReadOnlyList<string>? hosts, bool httpsOnly)
    {
        // Browsers drop tabs and line breaks inside a URL and read a backslash as a slash, so the link is read as they would read it.
        var link = Unbroken(found.Trim()).Replace('\\', '/');

        if (link.Length == 0)
        {
            return "it is empty; give the URL or path it leads to.";
        }

        if (link.StartsWith('#') || link.StartsWith('?') || (link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal)))
        {
            return null;
        }

        if (link.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            link = "https://" + link;
        }

        if (!link.Contains(':', StringComparison.Ordinal) || !Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return "it is neither an absolute URL, such as https://example.com/page, nor a path on this site, such as /products/kettle.";
        }

        if (!Schemes.Contains(uri.Scheme))
        {
            return $"links use http, https, mailto or tel, and '{uri.Scheme}:' is none of them.";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return "a web link names a host, such as https://example.com/page.";
        }

        if (httpsOnly && uri.Scheme == "http")
        {
            return "use https instead of http.";
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return hosts is null || hosts.Any(allowed => host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal))
            ? null
            : $"link only to {string.Join(", ", hosts)}, and {host} is not one of them.";
    }

    // Markdown and HTML links first, then bare URLs outside them, in the order they appear. A script URL that none of them holds is
    // added as well, wherever it is.
    private static IEnumerable<string> Links(string text)
    {
        var taken = new List<(int Start, int End)>();
        var links = new List<(int Start, string Link)>();

        foreach (var regex in new[] { MarkdownLink, MarkdownDefinition, HtmlLink, AutoLink })
        {
            foreach (Match match in regex.Matches(text))
            {
                taken.Add((match.Index, match.Index + match.Length));
                links.Add((match.Index, match.Groups.Values.Skip(1).First(group => group.Success).Value));
            }
        }

        links.AddRange(BareLink().Matches(text)
            .Where(match => !taken.Any(span => match.Index >= span.Start && match.Index < span.End))
            .Select(match => (match.Index, match.Value.TrimEnd('.', ',', ';', ':', '!', '?'))));

        if (ScriptUrl.Match(Unbroken(text)) is { Success: true } script && !links.Any(link => ScriptUrl.IsMatch(Unbroken(link.Link))))
        {
            links.Add((script.Index, script.Value));
        }

        return links.OrderBy(link => link.Start).Select(link => link.Link);
    }

    private static string Unbroken(string text)
    {
        return text.Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    // A host in the form links are compared in, punycode and lowercase, or null when it is no host name.
    private static string? AsciiHost(string host)
    {
        try
        {
            var ascii = new IdnMapping().GetAscii(host.Trim()).ToLowerInvariant();
            return Uri.CheckHostName(ascii) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6 ? ascii : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // The scheme is bounded, so a long run of letters and hyphens is not read again from every position.
    [GeneratedRegex(@"(?<![\w@/.])(?:[a-z][a-z0-9+.\-]{0,31}://|www\.)[^\s<>()""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BareLink();
}
