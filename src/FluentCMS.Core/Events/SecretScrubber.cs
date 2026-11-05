using System.Text.RegularExpressions;

namespace FluentCMS.Core.Events;

// Best effort, by shape, for text that comes out of a handler or a job and is stored in an event or the outbox: a URL is
// reduced to its scheme and host, since its path and query carry webhook tokens, a Bearer, Basic or ApiKey credential is
// masked, so is anything shaped like a FluentCMS API key, and so is the value of a key=value pair whose key names a secret.
// A secret in any other shape is not recognised.
internal static partial class SecretScrubber
{
    public static string Of(string text)
    {
        text = Url().Replace(text, "${scheme}${host}");
        text = Credential().Replace(text, "$1 ***");
        text = ApiKey().Replace(text, "${marker}***");
        return Pair().Replace(text, "${key}${quote}${separator}***");
    }

    // Whether a name, such as that of a detail of an activity row, suggests that its value is a secret.
    public static bool NamesSecret(string name) => SecretName().IsMatch(name);

    [GeneratedRegex(@"token|secret|passw(?:or)?d|pwd|key|authorization|credential|signature", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    [GeneratedRegex(@"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*://)(?:[^/?#@\s]*@)?(?<host>[^/?#\s]*)\S*")]
    private static partial Regex Url();

    [GeneratedRegex(@"\b(Bearer|Basic|ApiKey)\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex Credential();

    [GeneratedRegex(@"(?<marker>\bfcms_[dpm]k_)[A-Za-z0-9_\-]+")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"(?<key>[\w.\-]*(?:token|secret|passw(?:or)?d|pwd|key|authorization|credential|signature)[\w.\-]*)(?<quote>[""']?)(?<separator>\s*[=:]\s*)(?:""[^""]*""|'[^']*'|[^\s,;&""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pair();
}
