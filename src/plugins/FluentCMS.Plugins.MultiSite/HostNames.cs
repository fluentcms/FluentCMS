using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;
using FluentCMS.Content;

namespace FluentCMS.Plugins.MultiSite;

// What a host name is here: a DNS name in lower case, an internationalized name as its A-label, with no scheme, port,
// path or wildcard, at most 253 characters. An IP address is not one, since an address names a server and not a site.
internal static class HostNames
{
    public const int MaxLength = 253;

    private const int MaxLabelLength = 63;

    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true };

    // What a URL has around its host: the slashes of a scheme or a path, a query, a fragment, a user, a space.
    private static readonly SearchValues<char> NotOfAName = SearchValues.Create("/\\?#@ \t\r\n");

    private static readonly SearchValues<char> OfALabel = SearchValues.Create("-0123456789abcdefghijklmnopqrstuvwxyz");

    private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

    // The name as it is stored, or the refusal of what was sent.
    public static string Parse(string? value, string path)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw Refused(path, MultiSiteErrorCodes.InvalidHostName, "A host name is needed, such as 'www.example.com'.");
        }

        if (value.Contains('*', StringComparison.Ordinal))
        {
            throw Refused(path, MultiSiteErrorCodes.WildcardHostName, "A host name has no wildcard; add each name the site answers to.");
        }

        if (value.AsSpan().ContainsAny(NotOfAName))
        {
            throw Refused(path, MultiSiteErrorCodes.InvalidHostName, "A host name has no scheme, user, path, query or space, such as 'www.example.com'.");
        }

        if (value.Contains(':', StringComparison.Ordinal))
        {
            // An IPv6 address has colons too, bare or in brackets.
            throw value.StartsWith('[') || IPAddress.TryParse(value, out _)
                ? Refused(path, MultiSiteErrorCodes.InvalidHostName, "An IP address is not a host name.")
                : Refused(path, MultiSiteErrorCodes.HostNameWithPort, "A host name has no port; a site answers on every port of its host names.");
        }

        if (Normalize(value) is not { } name)
        {
            throw Refused(path, MultiSiteErrorCodes.InvalidHostName,
                $"A host name is labels of letters, digits and hyphens joined by dots, each of 1 to {MaxLabelLength} characters, up to {MaxLength} in all, and not an IP address.");
        }

        return name;
    }

    // The stored form of a well-formed name, or null. A request's host is looked up by it, so a name that could not be
    // stored finds nothing.
    public static string? Normalize(string value)
    {
        var name = value;
        if (!Ascii.IsValid(name))
        {
            try
            {
                name = Idn.GetAscii(name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // A name in lower case already is returned as it is, so one in the stored form allocates nothing here.
        name = name.ToLowerInvariant();
        if (name.Length is 0 or > MaxLength)
        {
            return null;
        }

        var last = ReadOnlySpan<char>.Empty;
        foreach (var label in name.AsSpan().Split('.'))
        {
            last = name.AsSpan()[label];
            if (!IsLabel(last))
            {
                return null;
            }
        }

        return last.ContainsAnyExcept(Digits) ? name : null;
    }

    // The name a request's Host header is looked up by. The root dot of a fully qualified name names the same host.
    public static string? OfRequest(string host) =>
        Normalize(host.Length > 1 && host[^1] == '.' ? host[..^1] : host);

    private static bool IsLabel(ReadOnlySpan<char> label) =>
        label.Length is > 0 and <= MaxLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && !label.ContainsAnyExcept(OfALabel);

    private static ValidationException Refused(string path, string code, string message) => new([new ValidationError(path, code, message)]);
}
