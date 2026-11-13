namespace FluentCMS.Cli;

// What the tool checks in an answer before it takes it as an instance's. Its client follows no redirect: HttpClient drops
// the key on one and would read whatever the other address answers, such as the sign-in page of a proxy, as the instance's.
internal static class InstanceAnswer
{
    public const string Json = "application/json";

    public static HttpMessageHandler Handler() => new HttpClientHandler { AllowAutoRedirect = false };

    public static bool IsRedirect(HttpResponseMessage response) => (int)response.StatusCode is >= 300 and <= 399;

    // Of the address it points to only the scheme, host and port, since its path and query can carry a sign-in's state.
    public static string Redirect(HttpResponseMessage response)
    {
        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        var location = response.Headers.Location;
        if (location is { IsAbsoluteUri: false } && response.RequestMessage?.RequestUri is { IsAbsoluteUri: true } requested)
        {
            location = new Uri(requested, location);
        }

        return location is { IsAbsoluteUri: true }
            ? $"{status}, a redirect to {location.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.StrongPort, UriFormat.UriEscaped)}, which the tool does not follow; give --url the address the instance answers at"
            : $"{status}, a redirect, which the tool does not follow; give --url the address the instance answers at";
    }

    public static bool Is(HttpResponseMessage response, string mediaType) =>
        string.Equals(response.Content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    public static string MediaTypeOf(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType ?? "no media type";
}
