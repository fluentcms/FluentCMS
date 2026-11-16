using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Admin.EndToEnd;

// A Playwright trace records the network with its bodies and headers, and each action with its arguments: the sign-in
// posts a password, the token endpoint answers tokens, and the hosted pages set the sign-in cookie. A kept trace holds only
// the entries of an allowlist: the event logs, the screenshots, and the recorded images, styles and fonts; the network log
// and every other body are left out. In what is kept, the secrets the context saw (passwords, tokens, codes, cookies) are
// masked as they are, and anything that looks like one (a JWS or JWE, a Data Protection payload, a field of the code flow)
// by its pattern. A trace in which any of them is still found, or whose masking fails, is not kept.
internal static partial class TraceScrubber
{
    private const string Mask = "[masked]";

    private static readonly HashSet<string> KeptResources = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".png", ".jpeg", ".jpg", ".webp", ".gif", ".svg", ".ico", ".woff", ".woff2", ".ttf",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpeg", ".jpg", ".webp", ".gif", ".ico", ".woff", ".woff2", ".ttf",
    };

    public static void Scrub(string source, string destination, IReadOnlyCollection<string> secrets)
    {
        var forms = Forms(secrets);
        File.Delete(destination);
        try
        {
            using (var input = ZipFile.OpenRead(source))
            using (var output = ZipFile.Open(destination, ZipArchiveMode.Create))
            {
                foreach (var entry in input.Entries)
                {
                    if (!Kept(entry.FullName))
                    {
                        continue;
                    }

                    var bytes = Read(entry);
                    if (IsText(entry.FullName))
                    {
                        bytes = Encoding.UTF8.GetBytes(MaskText(Encoding.UTF8.GetString(bytes), forms));
                    }

                    using var write = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                    write.Write(bytes);
                }
            }

            if (Leaks(destination, secrets) is { } leak)
            {
                throw new InvalidOperationException($"The trace still held {leak} after it was masked, so it was not kept.");
            }
        }
        catch
        {
            File.Delete(destination);
            throw;
        }
    }

    // What a kept trace may not hold: an entry off the allowlist, a secret in any entry, or the pattern of one in a text entry.
    public static string? Leaks(string trace, IReadOnlyCollection<string> secrets)
    {
        var forms = Forms(secrets);
        using var archive = ZipFile.OpenRead(trace);
        foreach (var entry in archive.Entries)
        {
            if (!Kept(entry.FullName))
            {
                return $"the entry {entry.FullName}, which is not on the allowlist";
            }

            var text = Encoding.UTF8.GetString(Read(entry));
            var latin = Encoding.Latin1.GetString(Read(entry));
            if (forms.Any(secret => text.Contains(secret, StringComparison.Ordinal) || latin.Contains(secret, StringComparison.Ordinal)))
            {
                return $"a secret in {entry.FullName}";
            }

            if (IsText(entry.FullName) && (Token().IsMatch(text) || Protected().IsMatch(text) || SignInParameter().IsMatch(text)))
            {
                return $"the pattern of a token in {entry.FullName}";
            }
        }

        return null;
    }

    // The event logs of each context and page, the screenshots, and the recorded images, styles and fonts.
    private static bool Kept(string name) =>
        LogEntry().IsMatch(name)
        || Screenshot().IsMatch(name)
        || (name.StartsWith("resources/", StringComparison.Ordinal) && name.IndexOf('/', "resources/".Length) < 0 && KeptResources.Contains(Path.GetExtension(name)));

    private static bool IsText(string name) => !BinaryExtensions.Contains(Path.GetExtension(name));

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var read = entry.Open();
        using var buffer = new MemoryStream();
        read.CopyTo(buffer);
        return buffer.ToArray();
    }

    // Each secret as it is, and as a URL, a form and JSON write it.
    private static List<string> Forms(IReadOnlyCollection<string> secrets) =>
        [.. secrets
            .SelectMany(secret => new[] { secret, WebUtility.UrlEncode(secret), Uri.EscapeDataString(secret), JsonSerializer.Serialize(secret)[1..^1] })
            .Where(secret => secret.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length)];

    private static string MaskText(string text, IReadOnlyList<string> secrets)
    {
        foreach (var secret in secrets)
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        text = Token().Replace(text, Mask);
        text = Protected().Replace(text, Mask);
        return SignInParameter().Replace(text, match => match.Groups["name"].Value + "=" + Mask);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+\.(?:trace|stacks)$")]
    private static partial Regex LogEntry();

    [GeneratedRegex(@"^(?:screencast|resources)/[^/]+\.jpe?g$")]
    private static partial Regex Screenshot();

    // A compact JWS or JWE, whose header is JSON in base64url.
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}(?:\.[A-Za-z0-9_-]*){1,4}")]
    private static partial Regex Token();

    // A payload of ASP.NET Core Data Protection, as the sign-in and antiforgery cookies and fields carry.
    [GeneratedRegex(@"CfDJ8[A-Za-z0-9_-]{16,}")]
    private static partial Regex Protected();

    // The secrets of the code flow and of the token endpoint's form, whatever their encoding.
    [GeneratedRegex(@"(?<name>\b(?:code|code_verifier|refresh_token|access_token|id_token|token|password|Input\.Password))=(?!\[masked\])[^&""\s\\]+")]
    private static partial Regex SignInParameter();
}
