using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Admin.EndToEnd;

// A Playwright trace records the network with its bodies and headers, and each action with its arguments: the sign-in
// posts a password, the token endpoint answers tokens, and the hosted pages set the sign-in cookie. Before a trace is
// kept, the network log and every recorded body but images, styles and fonts are left out, and the passwords of the test,
// tokens (JWS and JWE, which start with "eyJ"), Data Protection payloads (cookies and antiforgery tokens) and the code
// and verifier of the sign-in are masked in what is left. A trace in which any of them is still found is not kept.
internal static partial class TraceScrubber
{
    private const string Mask = "[masked]";

    private static readonly HashSet<string> KeptResources = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".png", ".jpeg", ".jpg", ".webp", ".gif", ".svg", ".ico", ".woff", ".woff2", ".ttf",
    };

    public static void Scrub(string source, string destination, IReadOnlyCollection<string> passwords)
    {
        var secrets = passwords
            .SelectMany(password => new[] { password, WebUtility.UrlEncode(password), Uri.EscapeDataString(password), JsonEncoded(password) })
            .Where(secret => secret.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length)
            .ToList();

        using (var input = ZipFile.OpenRead(source))
        using (var output = ZipFile.Open(destination, ZipArchiveMode.Create))
        {
            foreach (var entry in input.Entries)
            {
                if (!Kept(entry.FullName))
                {
                    continue;
                }

                using var read = entry.Open();
                using var buffer = new MemoryStream();
                read.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (IsText(entry.FullName))
                {
                    bytes = Encoding.UTF8.GetBytes(MaskText(Encoding.UTF8.GetString(bytes), secrets));
                }

                using var write = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                write.Write(bytes);
            }
        }

        if (Leaks(destination, secrets) is { } leak)
        {
            File.Delete(destination);
            throw new InvalidOperationException($"The trace still held {leak} after it was masked, so it was not kept.");
        }
    }

    // What a kept trace may not hold, for the test that checks a real one.
    public static string? Leaks(string trace, IReadOnlyCollection<string> secrets)
    {
        using var archive = ZipFile.OpenRead(trace);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith(".network", StringComparison.Ordinal))
            {
                return $"the network log {entry.FullName}";
            }

            using var read = entry.Open();
            using var buffer = new MemoryStream();
            read.CopyTo(buffer);
            var text = Encoding.UTF8.GetString(buffer.ToArray());
            if (secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal)))
            {
                return $"a password in {entry.FullName}";
            }

            if (IsText(entry.FullName) && (Token().IsMatch(text) || Protected().IsMatch(text) || SignInParameter().IsMatch(text)))
            {
                return $"a token in {entry.FullName}";
            }
        }

        return null;
    }

    private static bool Kept(string name) =>
        !name.EndsWith(".network", StringComparison.Ordinal)
        && (!name.StartsWith("resources/", StringComparison.Ordinal) || KeptResources.Contains(Path.GetExtension(name)));

    private static bool IsText(string name) => !name.StartsWith("resources/", StringComparison.Ordinal) || name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

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

    private static string JsonEncoded(string text)
    {
        var json = JsonSerializer.Serialize(text);
        return json[1..^1];
    }

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
