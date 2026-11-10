using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Plugins.Media;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.GraphQL.Tests;

// A host with the Blog sample and its seed set: two authors, three tags, and articles of which two are published in en,
// one of them also in fr. Its media is in a directory of its own, removed with it. The tests of a class share one, each
// with roles, keys and entries of its own.
public class BlogHost : IAsyncDisposable
{
    public const string Endpoint = "/graphql";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-graphql-");

    public BlogHost()
        : this(null, null)
    {
    }

    protected BlogHost(Action<GraphQLOptions>? options, Action<IServiceCollection>? services)
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithServices(collection =>
            {
                collection.PostConfigure<MediaOptions>(media => media.Local.Root = _directory.FullName);
                if (options is not null)
                {
                    collection.PostConfigure(options);
                }

                services?.Invoke(collection);
            })
            .Configure(cms =>
            {
                cms.AddBlogSample();
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
    }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public FluentCmsTestHost Host { get; }

    public HttpClient Anonymous() => Host.CreateClient();

    public async Task<HttpClient> AdminAsync() =>
        Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    // A key of a new role with the permissions given, each a name or a pattern.
    public async Task<HttpClient> KeyAsync(ApiKeyClass keyClass, params string[] permissions)
    {
        var name = "role-" + Guid.NewGuid().ToString("N");
        await Host.CreateRoleAsync(name, permissions, Token);
        return Host.AsKey(await Host.CreateApiKeyAsync(keyClass, name, cancellationToken: Token));
    }

    // A key of a new role with the grants given, written through role management as an administrator would, so that a
    // grant may carry constraints.
    public async Task<HttpClient> KeyWithGrantsAsync(ApiKeyClass keyClass, params object[] grants)
    {
        using var admin = await AdminAsync();
        var name = "role-" + Guid.NewGuid().ToString("N");
        using var role = await admin.PostAsJsonAsync(new Uri("/api/management/v1/roles", UriKind.Relative), new { name, grants }, Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        return Host.AsKey(await Host.CreateApiKeyAsync(keyClass, name, cancellationToken: Token));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}

// The classes that only read the seed set, or write entries of their own, share one host.
[CollectionDefinition(Name)]
public sealed class BlogTests : ICollectionFixture<BlogHost>
{
    public const string Name = "blog";
}

internal static class GraphQLClient
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The response of a query: its HTTP status and its body, whose data and errors the test reads.
    public static async Task<(HttpStatusCode Status, JsonNode Body)> SendAsync(this HttpClient client, string query, object? variables = null, string path = BlogHost.Endpoint)
    {
        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { query, variables }, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response.StatusCode, JsonNode.Parse(text) ?? throw new InvalidOperationException($"The response {(int)response.StatusCode} has no JSON body: {text}"));
    }

    // The data of a query that ran with no error.
    public static async Task<JsonNode> QueryAsync(this HttpClient client, string query, object? variables = null)
    {
        var (status, body) = await client.SendAsync(query, variables);
        status.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        body["errors"].ShouldBeNull(body.ToJsonString());
        return body["data"]!;
    }

    // The errors of a query, with its data, which may be partial.
    public static async Task<(JsonNode? Data, JsonArray Errors)> FailAsync(this HttpClient client, string query, object? variables = null)
    {
        var (_, body) = await client.SendAsync(query, variables);
        body["errors"].ShouldNotBeNull(body.ToJsonString());
        return (body["data"], body["errors"]!.AsArray());
    }

    public static string Code(this JsonNode? error) => error!["extensions"]!["code"]!.GetValue<string>();

    public static List<string> Codes(this JsonArray errors) => [.. errors.Select(error => error.Code())];

    public static List<string> Strings(this JsonNode? items, string field) => [.. items!.AsArray().Select(item => item![field]!.GetValue<string>())];
}
