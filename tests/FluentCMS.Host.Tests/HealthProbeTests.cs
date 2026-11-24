using System.Net;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The image's HEALTHCHECK, against the reference host set up as docs/reverse-proxy.md advises: the HTTPS redirect on, and
// the path base in appsettings.json rather than in a variable.
public sealed class HealthProbeTests : IAsyncDisposable
{
    private const string Settings = """
        { "FluentCms": { "ReverseProxy": { "PathBase": "/cms", "HttpsRedirection": { "Enabled": true, "Port": 443 } } } }
        """;

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly HostUnderTest _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task TheProbeSucceedsWithTheRedirectOnAndThePathBaseInJson()
    {
        _host.WriteFile("appsettings.json", Settings);
        var app = await _host.StartAsync();

        var exitCode = await HealthProbe.RunAsync(HealthProbe.Configuration(_host.Root), TimeSpan.FromSeconds(30), app.GetTestServer().CreateHandler());

        exitCode.ShouldBe(0);
    }

    // The probe reads the path base the host serves under, and the redirect still holds outside /health.
    [Fact]
    public async Task WithoutThePathBaseTheProbeFailsAndTheApiIsStillRedirected()
    {
        _host.WriteFile("appsettings.json", Settings);
        var app = await _host.StartAsync();
        using var other = new EmptyDirectory();

        var exitCode = await HealthProbe.RunAsync(HealthProbe.Configuration(other.Path), TimeSpan.FromSeconds(30), app.GetTestServer().CreateHandler());
        using var client = _host.Client(new Uri("http://localhost/"));
        using var api = await client.GetAsync(new Uri("/cms/api/delivery/v1/article", UriKind.Relative), Token);
        using var live = await client.GetAsync(new Uri("/cms/health/live", UriKind.Relative), Token);

        exitCode.ShouldBe(1);
        api.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        api.Headers.Location!.ToString().ShouldBe("https://localhost/cms/api/delivery/v1/article");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ThePortAndThePathBaseAreReadAsTheHostReadsThem()
    {
        using var root = new EmptyDirectory();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "appsettings.json"), """
            { "urls": "https://+:5443;http://+:5080", "FluentCms": { "ReverseProxy": { "PathBase": "/cms" } } }
            """);
        using var handler = new Recorder();

        var exitCode = await HealthProbe.RunAsync(HealthProbe.Configuration(root.Path), TimeSpan.FromSeconds(30), handler);

        exitCode.ShouldBe(0);
        handler.Requested.ShouldBe(new Uri("http://localhost:5080/cms/health/ready"));
    }

    [Fact]
    public async Task AnHttpEndpointOfKestrelWinsOverUrls()
    {
        using var root = new EmptyDirectory();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "appsettings.json"), """
            {
              "urls": "http://+:5080",
              "Kestrel": { "Endpoints": { "Https": { "Url": "https://*:5443" }, "Http": { "Url": "http://*:5090" } } }
            }
            """);
        using var handler = new Recorder();

        var exitCode = await HealthProbe.RunAsync(HealthProbe.Configuration(root.Path), TimeSpan.FromSeconds(30), handler);

        exitCode.ShouldBe(0);
        handler.Requested.ShouldBe(new Uri("http://localhost:5090/health/ready"));
    }

    // Kestrel ignores urls and http_ports once endpoints are configured; the probe still falls back to them.
    [Fact]
    public async Task WithHttpsOnlyEndpointsTheProbeFallsBackToUrlsAndThenHttpPorts()
    {
        using var root = new EmptyDirectory();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "appsettings.json"), """
            { "Kestrel": { "Endpoints": { "Https": { "Url": "https://*:5443" } } }, "http_ports": "5081" }
            """);
        using var handler = new Recorder();

        var exitCode = await HealthProbe.RunAsync(HealthProbe.Configuration(root.Path), TimeSpan.FromSeconds(30), handler);

        exitCode.ShouldBe(0);
        handler.Requested.ShouldBe(new Uri("http://localhost:5081/health/ready"));
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class EmptyDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-probe-tests-");

        public string Path => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
