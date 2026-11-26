using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Admin.EndToEnd;

// A Playwright trace records the network with its bodies and headers, and each action with its arguments: the sign-in
// posts a password, the token endpoint answers tokens, and the hosted pages set the sign-in cookie. A kept trace holds only
// the entries of an allowlist: the event logs, the screenshots, and the recorded images, styles and fonts; the network log
// and every other body are left out. In the text entries, each secret the context saw (passwords, tokens, codes, the
// antiforgery field, cookies; values of 8 characters or more) is masked in these forms: as it is, percent-encoded
// (Uri.EscapeDataString and WebUtility.UrlEncode), form-encoded as a browser posts it (a space as +, ! as %21), escaped
// for JSON, and with its quotes and backslashes escaped as JavaScript writes them. Anything with the pattern of a JWS or
// JWE, a Data Protection payload or a field of the code flow is masked too. Base64, UTF-16 and a secret split across
// events are not checked. A secret no pattern finds, such as an authenticator key or a recovery code, is masked only once the
// case gives it (AdminBrowser.Mask), and a screenshot is not masked at all, so the steps that show such a secret are not
// recorded (AdminBrowser.UnrecordedAsync): the chunks recorded around them are joined into one trace here, the event logs of
// each under a name of its own, which the trace viewer shows as one timeline. The masked trace is built beside the raw one,
// outside the folder CI uploads, and moved there only once no secret in those forms, and no pattern, is found in any entry;
// otherwise it is deleted and not kept.
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

    public static void Scrub(string source, string destination, IReadOnlyCollection<string> secrets) => Scrub([source], destination, secrets);

    // The chunks of one context, in the order they were recorded, as one trace. A resource a later chunk holds again, as a
    // style the pages kept using, is kept once.
    public static void Scrub(IReadOnlyList<string> sources, string destination, IReadOnlyCollection<string> secrets)
    {
        var forms = Forms(secrets);
        var masked = sources[0] + ".masked";
        File.Delete(masked);
        try
        {
            using (var output = ZipFile.Open(masked, ZipArchiveMode.Create))
            {
                var written = new HashSet<string>(StringComparer.Ordinal);
                for (var chunk = 0; chunk < sources.Count; chunk++)
                {
                    using var input = ZipFile.OpenRead(sources[chunk]);
                    foreach (var entry in input.Entries)
                    {
                        var name = chunk > 0 && LogEntry().IsMatch(entry.FullName) ? $"chunk{chunk}-{entry.FullName}" : entry.FullName;
                        if (!Kept(entry.FullName) || !written.Add(name))
                        {
                            continue;
                        }

                        var bytes = Read(entry);
                        if (IsText(entry.FullName))
                        {
                            bytes = Encoding.UTF8.GetBytes(MaskText(Encoding.UTF8.GetString(bytes), forms));
                        }

                        using var write = output.CreateEntry(name, CompressionLevel.Optimal).Open();
                        write.Write(bytes);
                    }
                }
            }

            if (Leaks(masked, secrets) is { } leak)
            {
                throw new InvalidOperationException($"The trace still held {leak} after it was masked, so it was not kept.");
            }

            File.Move(masked, destination, overwrite: true);
        }
        finally
        {
            File.Delete(masked);
        }
    }

    // What a kept trace may not hold: an entry off the allowlist, or a secret or the pattern of one in any entry.
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

            if (new[] { text, latin }.Any(content => Token().IsMatch(content) || Protected().IsMatch(content) || SignInParameter().IsMatch(content)))
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

    private static List<string> Forms(IReadOnlyCollection<string> secrets) =>
        [.. secrets
            .SelectMany(secret => new[]
            {
                secret,
                WebUtility.UrlEncode(secret),
                Uri.EscapeDataString(secret),
                Uri.EscapeDataString(secret).Replace("%20", "+", StringComparison.Ordinal),
                JsonSerializer.Serialize(secret)[1..^1],
                secret.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal),
            })
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
