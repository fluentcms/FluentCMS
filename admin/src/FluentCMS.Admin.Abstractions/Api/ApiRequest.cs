using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Api;

/// <summary>
/// A request to the Management API. Sending the same instance again, as a retry after a network error does, sends the
/// same <see cref="IdempotencyKey"/>, so that the server answers what it answered the first time.
/// </summary>
public sealed class ApiRequest
{
    /// <summary>The <c>If-Match</c> of a save that adds a locale the entry does not have yet.</summary>
    public const string NewLocale = "\"0\"";

    /// <summary>Creates a request.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">
    /// The path under <c>/api/management/v1/</c>, with its query string, such as <c>content/article?page=2</c>. It may not
    /// start with a slash, whitespace or a control character, hold a backslash or an encoded slash or backslash before its
    /// query, or lead out of the Management API.
    /// </param>
    public ApiRequest(HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrEmpty(path);
        // An encoded slash or backslash before the query is refused too: a front end that decodes it before routing would
        // resolve it out of the Management API.
        var beforeQuery = path.Split('?', 2)[0];
        if (path[0] == '/' || char.IsWhiteSpace(path[0]) || path.Any(char.IsControl) || path.Contains('\\', StringComparison.Ordinal)
            || beforeQuery.Contains("%2f", StringComparison.OrdinalIgnoreCase) || beforeQuery.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"'{path}' is not a path relative to /api/management/v1/, such as 'content/article'.", nameof(path));
        }

        Method = method;
        Path = path;
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The path under <c>/api/management/v1/</c>, with its query string.</summary>
    public string Path { get; }

    /// <summary>The JSON body, if any.</summary>
    public JsonNode? Body { get; init; }

    /// <summary>
    /// The entity tag of the read a save is based on, sent as <c>If-Match</c>; <see cref="NewLocale"/> adds a locale. A weak
    /// tag is sent by its value.
    /// </summary>
    public string? IfMatch { get; init; }

    /// <summary>
    /// The <c>Idempotency-Key</c> of the user action a create belongs to, from <see cref="NewIdempotencyKey"/>. The server
    /// keys it with the site and the locale, so a keyed request names both, or <see cref="NoSite"/> and <see cref="NoLocale"/>,
    /// as they were when the action started.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>The site of this request, instead of the shell's. A request that names its site sends only the locale it names.</summary>
    public string? Site { get; init; }

    /// <summary>Whether the request sends no <c>X-Cms-Site</c>, for the instance's default site whatever the shell shows.</summary>
    public bool NoSite { get; init; }

    /// <summary>The content locale of this request, instead of the view's.</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Whether the request sends no <c>X-Cms-Locale</c>, for a call that is not about content, such as <c>me</c> or
    /// <c>schema</c>: the server refuses a locale its site does not allow, so the view's locale could fail it after a site switch.
    /// </summary>
    public bool NoLocale { get; init; }

    /// <summary>A new key for one user action, to keep until the action has an answer.</summary>
    /// <returns>A key the server accepts.</returns>
    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");
}
