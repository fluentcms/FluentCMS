namespace FluentCMS.Cli;

// What the tool checks in an answer before it takes it as an instance's. Its client follows no redirect: HttpClient drops
// the key on one and would read whatever the other address answers, such as the sign-in page of a proxy, as the instance's.
internal static class InstanceAnswer
{
    public const string Json = "application/json";

    public static HttpMessageHandler Handler() => new HttpClientHandler { AllowAutoRedirect = false };

    public static bool IsRedirect(HttpResponseMessage response) => (int)response.StatusCode is >= 300 and <= 399;

    // Of the address it points to only the scheme, host and port, since its path and query can carry a sign-in's state; a
    // redirect on the origin of --url names none.
    public static string Redirect(HttpResponseMessage response)
    {
        const UriComponents Origin = UriComponents.Scheme | UriComponents.Host | UriComponents.StrongPort;
        var status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
        var requested = response.RequestMessage?.RequestUri is { IsAbsoluteUri: true } uri ? uri : null;
        var location = response.Headers.Location;
        if (location is { IsAbsoluteUri: false } && requested is not null)
        {
            location = new Uri(requested, location);
        }

        if (location is not { IsAbsoluteUri: true })
        {
            return $"{status}, a redirect, which the tool does not follow; give --url the address the instance answers at";
        }

        if (requested is not null && Uri.Compare(location, requested, Origin, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return $"{status}, a redirect to its own host, which the tool does not follow; check the path of --url, or a proxy in front of it";
        }

        return $"{status}, a redirect to {location.GetComponents(Origin, UriFormat.UriEscaped)}, which the tool does not follow; give --url the address the instance answers at";
    }

    public static bool Is(HttpResponseMessage response, string mediaType) =>
        string.Equals(response.Content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase);

    public static string MediaTypeOf(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType ?? "no media type";
}
