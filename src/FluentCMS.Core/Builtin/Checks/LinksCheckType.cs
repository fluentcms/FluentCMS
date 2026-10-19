using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Builtin.Checks;

// Every link in the text is one a reader can follow (CHK-01): an absolute http, https, mailto or tel URL, or a path or fragment on
// the site. Links are found in Markdown, in HTML href and src attributes, and as bare URLs. 'hosts' limits web links to those
// hosts and their subdomains, and 'httpsOnly' refuses plain http. Whether a page answers is not asked: that needs outbound
// requests, which the administrator allow-lists (SEC-05).
internal sealed partial class LinksCheckType : ICheckType
{
    private static readonly string[] Schemes = ["http", "https", "mailto", "tel"];

    public string Name => "links";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        var hosts = reader.OptionalStringList("hosts");
        reader.Flag("httpsOnly");

        if (hosts?.FirstOrDefault(host => Uri.CheckHostName(host) == UriHostNameType.Unknown || host != host.ToLowerInvariant()) is { } invalid)
        {
            reader.Invalid("hosts", $"must hold lowercase host names such as 'example.com', and '{invalid}' is not one");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var reader = new CheckSettingsReader(Name, input.Settings);
        var hosts = reader.OptionalStringList("hosts");
        var httpsOnly = reader.Flag("httpsOnly");
        var findings = new List<CheckFinding>();

        foreach (var (pointer, text) in CheckContent.Strings(input.Subject.Fields))
        {
            foreach (var link in Links(text))
            {
                if (Problem(link, hosts, httpsOnly) is { } problem)
                {
                    findings.Add(new CheckFinding($"Fix the link '{CheckContent.Quote(link)}' in {CheckContent.FieldOf(pointer)}: {problem}", pointer, link));
                }
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static string? Problem(string link, IReadOnlyList<string>? hosts, bool httpsOnly)
    {
        if (link.Length == 0)
        {
            return "it is empty; give the URL or path it leads to.";
        }

        if (link.StartsWith('#') || link.StartsWith('?') || (link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal)))
        {
            return null;
        }

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !link.Contains(':', StringComparison.Ordinal))
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

    // Markdown links and images, HTML attributes and autolinks first, then bare URLs outside them.
    private static IEnumerable<string> Links(string text)
    {
        var taken = new List<(int Start, int End)>();
        var links = new List<(int Start, string Link)>();

        foreach (var match in MarkdownLink().Matches(text).Concat(HtmlLink().Matches(text)).Concat(AutoLink().Matches(text)))
        {
            var group = match.Groups.Values.Skip(1).First(candidate => candidate.Success);
            taken.Add((match.Index, match.Index + match.Length));
            links.Add((group.Index, group.Value));
        }

        foreach (Match match in BareLink().Matches(text))
        {
            if (!taken.Any(span => match.Index >= span.Start && match.Index < span.End))
            {
                links.Add((match.Index, match.Value.TrimEnd('.', ',', ';', ':', '!', '?')));
            }
        }

        return links.OrderBy(link => link.Start).Select(link => link.Link);
    }

    // A destination may hold one level of balanced parentheses, as CommonMark allows, and a title in quotes or parentheses.
    [GeneratedRegex(
        @"\[(?:[^\[\]]|\[[^\[\]]*\])*\]\(\s*<?((?:[^\s<>()]|\([^\s<>()]*\))*)>?(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\b(?:href|src)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex HtmlLink();

    [GeneratedRegex(@"<((?:https?|mailto|tel):[^\s<>]+)>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"(?<![\w@/.])(?:[a-z][a-z0-9+.\-]*://|www\.)[^\s<>()""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BareLink();
}
