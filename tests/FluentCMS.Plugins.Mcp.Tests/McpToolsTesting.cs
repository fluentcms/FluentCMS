using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Plugins.Media;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The host the tool tests of a class share: the MCP, media and sample plugins on the types of ToolContent, with the media
// files in a directory of its own and the URLs an upload fetches answered by the test. It has no search index.
public sealed class McpToolsFixture : IAsyncDisposable
{
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-mcp-media-");

    public McpToolsFixture() => Host = McpToolsTesting.Host(_media.FullName, Downloads);

    public FakeDownloads Downloads { get; } = new();

    public FluentCmsTestHost Host { get; }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        Downloads.Dispose();
        _media.Delete(recursive: true);
    }
}

// What a media_upload fetches, in place of the network: the answer of each URL, and the URLs that were asked for.
public sealed class FakeDownloads : HttpMessageHandler
{
    public ConcurrentDictionary<string, Func<HttpResponseMessage>> Answers { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Requested { get; } = new();

    public void Serve(string url, byte[] content) =>
        Answers[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    public void Redirect(string url, string location) =>
        Answers[url] = () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Requested.Enqueue(url);
        return Task.FromResult(Answers.TryGetValue(url, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal static class McpToolsTesting
{
    public const string Roles = "/api/management/v1/roles";

    public static FluentCmsTestHost Host(string mediaRoot, FakeDownloads downloads) =>
        new FluentCmsTestHost()
            .WithContentTypes(typeof(Post), typeof(Writer), typeof(Masthead))
            .WithServices(services =>
            {
                services.PostConfigure<MediaOptions>(options => options.Local.Root = mediaRoot);
                services.AddSingleton(new McpDownloadTransport(downloads));
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });

    public static object Grant(string permission, params string[] fields) =>
        fields.Length == 0 ? new { permission } : new { permission, constraints = new { fields } };

    // A key of a new role with the grants given, written through role management as an administrator would.
    public static async Task<(HttpClient Client, TestApiKey Key)> RoleKeyAsync(FluentCmsTestHost host, ApiKeyClass keyClass, params object[] grants)
    {
        using var admin = await KeyAsync(host);
        var name = "role-" + Guid.NewGuid().ToString("N");
        using var role = await admin.PostAsJsonAsync(new Uri(Roles, UriKind.Relative), new { name, grants }, Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        var key = await host.CreateApiKeyAsync(keyClass, name, cancellationToken: Token);
        return (host.AsKey(key), key);
    }

    public static async Task<HttpClient> RoleAsync(FluentCmsTestHost host, params object[] grants) =>
        (await RoleKeyAsync(host, ApiKeyClass.Management, grants)).Client;

    // The arguments as the protocol's client sends them, whatever their shape.
    public static Task<CallToolResult> CallAsync(McpClient client, string tool, object? arguments = null)
    {
        Dictionary<string, object?> sent = [];
        if (arguments is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(arguments).EnumerateObject())
            {
                sent[property.Name] = property.Value;
            }
        }

        return client.CallToolAsync(tool, sent, cancellationToken: Token).AsTask();
    }

    // The structured content of a call that succeeded.
    public static async Task<JsonElement> OkAsync(McpClient client, string tool, object? arguments = null)
    {
        var result = await CallAsync(client, tool, arguments);
        result.IsError.ShouldNotBe(true, Text(result));
        return result.StructuredContent.ShouldNotBeNull();
    }

    public static async Task<Dictionary<string, Tool>> ToolsAsync(McpClient client) =>
        (await client.ListToolsAsync(cancellationToken: Token)).ToDictionary(tool => tool.Name, tool => tool.ProtocolTool, StringComparer.Ordinal);

    public static List<string> Of(Dictionary<string, Tool> tools, string prefix) =>
        [.. tools.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
}
