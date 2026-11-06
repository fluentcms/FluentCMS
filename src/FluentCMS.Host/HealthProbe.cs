namespace FluentCMS.Host;

// The chiseled image has no shell or curl, so its HEALTHCHECK runs the host itself with this argument: a request to
// /health/ready on the container's own HTTP port, under the path base, that exits 0 when the host is ready.
internal static class HealthProbe
{
    public const string Argument = "--probe";

    private static readonly char[] Separators = [';', ','];

    public static async Task<int> RunAsync(TimeSpan timeout)
    {
        var pathBase = Environment.GetEnvironmentVariable("FluentCms__ReverseProxy__PathBase") ?? "";

        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(new Uri($"http://localhost:{Port()}{pathBase}/health/ready"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return 1;
        }
    }

    // ASPNETCORE_URLS wins over ASPNETCORE_HTTP_PORTS in Kestrel, so it does here too.
    private static string Port()
    {
        var url = Split("ASPNETCORE_URLS").FirstOrDefault(value => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (url is not null && Uri.TryCreate(url.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal), UriKind.Absolute, out var uri))
        {
            return uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Split("ASPNETCORE_HTTP_PORTS").FirstOrDefault() ?? "8080";
    }

    private static string[] Split(string variable) =>
        Environment.GetEnvironmentVariable(variable)?.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
