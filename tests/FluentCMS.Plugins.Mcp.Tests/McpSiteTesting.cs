using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using Role = FluentCMS.Core.Storage.Entities.Role;
using RoleGrant = FluentCMS.Core.Storage.Entities.RoleGrant;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The host the tests of the schema resources, the instructions and the stored prompts share: the MCP plugin and the
// sample plugin on two content types. A test keeps what it saves in a site of its own.
public sealed class McpSiteFixture : IAsyncDisposable
{
    public FluentCmsTestHost Host { get; } = McpSiteTesting.Host();

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

internal static class McpSiteTesting
{
    public const string Instructions = "/api/management/v1/mcp/instructions";
    public const string Prompts = "/api/management/v1/mcp/prompts";

    public static FluentCmsTestHost Host() =>
        new FluentCmsTestHost()
            .WithContentTypes<Article, Page>()
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });

    // A Management key of the role whose every request names the site.
    public static async Task<HttpClient> KeyInAsync(FluentCmsTestHost host, Guid site, string role = BuiltInRoles.SuperAdminName)
    {
        var client = await KeyAsync(host, role: role);
        client.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());
        return client;
    }

    // An instance-wide role with these grants, each a permission and the constraints of it, if any.
    public static async Task<string> CreateRoleAsync(FluentCmsTestHost host, params (string Permission, PermissionConstraints? Constraints)[] grants)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = new Role { Id = Guid.CreateVersion7(), Name = $"role-{Guid.NewGuid():N}" };
        context.Set<Role>().Add(role);
        context.Set<RoleGrant>().AddRange(grants.Select(grant => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = grant.Permission, Constraints = grant.Constraints?.ToJson() }));
        await context.SaveChangesAsync(Token);
        return role.Name;
    }

    public static async Task<JsonNode?> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    // The paths and codes of a refusal that names its errors.
    public static List<(string Path, string Code)> Errors(JsonNode? problem) =>
        [.. problem!["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()))];

    public static async Task<string> ReadAsync(McpClient client, string uri) =>
        (await client.ReadResourceAsync(uri, cancellationToken: Token)).Contents.ShouldHaveSingleItem().ShouldBeOfType<TextResourceContents>().Text;

    public static async Task<string> RefusalAsync(McpClient client, string uri) =>
        (await Should.ThrowAsync<McpProtocolException>(async () => await client.ReadResourceAsync(uri, cancellationToken: Token))).Message;

    // The activity rows of the site of the client with the action, oldest first, once the outbox has delivered them.
    public static async Task<List<JsonNode>> ActivityAsync(FluentCmsTestHost host, HttpClient admin, string action)
    {
        await DrainOutboxAsync(host);
        var rows = await SendAsync(admin, HttpMethod.Get, $"/api/management/v1/activity?action={action}");
        return [.. rows!["items"]!.AsArray().Select(row => row!).Reverse()];
    }
}
