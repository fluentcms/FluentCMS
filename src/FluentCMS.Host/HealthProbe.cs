namespace FluentCMS.Host;

// The chiseled image has no shell or curl, so its HEALTHCHECK runs the host itself with this argument: a request to
// /health/ready on the container's own HTTP port, under the path base, that exits 0 when the host is ready.
internal static class HealthProbe
{
    public const string Argument = "--probe";

    private const string PathBaseKey = "FluentCms:ReverseProxy:PathBase";

    private static readonly char[] Separators = [';', ','];

    public static Task<int> RunAsync(TimeSpan timeout) => RunAsync(Configuration(), timeout);

    // Read as the host reads it, so that appsettings.json, its environment's file and any casing of a variable count.
    internal static IConfiguration Configuration(string? contentRoot = null) =>
        WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot }).Configuration;

    // A redirect is not followed: /health is never redirected to https, and following one elsewhere would test another host.
    internal static async Task<int> RunAsync(IConfiguration configuration, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        var pathBase = configuration[PathBaseKey] ?? "";

        using var client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(new Uri($"http://localhost:{Port(configuration)}{pathBase}/health/ready"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return 1;
        }
    }

    // The first http endpoint of Kestrel:Endpoints, else of urls, else http_ports; ASPNETCORE_URLS and ASPNETCORE_HTTP_PORTS
    // reach the host as urls and http_ports, which is also where a JSON file puts them. Kestrel ignores urls and http_ports
    // whenever endpoints are configured, even with no http one among them, which the probe does not follow.
    private static string Port(IConfiguration configuration)
    {
        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(endpoint => endpoint["Url"]);
        if (FirstHttpPort(endpoints) is { } endpointPort)
        {
            return endpointPort;
        }

        return FirstHttpPort(Split(configuration["urls"])) ?? Split(configuration["http_ports"]).FirstOrDefault() ?? "8080";
    }

    private static string? FirstHttpPort(IEnumerable<string?> urls)
    {
        var url = urls.FirstOrDefault(value => value?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true);
        return url is not null && Uri.TryCreate(url.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            ? uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string[] Split(string? value) =>
        value?.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
