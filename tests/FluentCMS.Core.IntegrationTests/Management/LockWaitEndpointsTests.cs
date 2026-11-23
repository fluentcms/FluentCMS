using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Hooks;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// #3573 over HTTP on a SQLite file: a write that waits past the lock timeout, here for a publish held in its after hook,
// answers 503 FCMS2411 with Retry-After and changes nothing, and its retry runs once the holder is done.
public sealed class LockWaitEndpointsTests : IAsyncLifetime
{
    private readonly HeldPublish _held = new();
    private FluentCmsTestHost _host = null!;
    private HttpClient _editor = null!;

    public async ValueTask InitializeAsync()
    {
        _host = new FluentCmsTestHost()
            .UseSqlite((string?)null)
            .WithContentTypes<Page, Person>()
            .Configure(cms => cms.Services.Configure<ContentWriteOptions>(options => options.LockTimeout = TimeSpan.FromSeconds(1)))
            .WithServices(services =>
            {
                services.AddSingleton(_held);
                services.AddScoped<IAfterPublishHook<ContentDocument>, HoldingHook>();
            });
        _editor = await _host.AsUserAsync(await _host.CreateUserAsync("Editor"));
    }

    public async ValueTask DisposeAsync()
    {
        _held.Release.TrySetResult();
        _editor.Dispose();
        await _host.DisposeAsync();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(HttpResponseMessage Response, JsonNode? Json)> SendAsync(HttpMethod method, string path, string? json = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(ManagementFixture.Root + path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        var response = await _editor.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    // A publish of the page that waits in its after hook, holding every row of the entry and on SQLite the database.
    private async Task<(string Id, Task<(HttpResponseMessage Response, JsonNode? Json)> Publish)> HeldPublishAsync()
    {
        var (created, json) = await SendAsync(HttpMethod.Post, "page", """{ "title": "Hello" }""");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = json!["id"]!.GetValue<string>();
        _held.Armed = true;
        var publish = SendAsync(HttpMethod.Post, $"page/{id}/publish", ifMatch: "\"1\"");
        await _held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return (id, publish);
    }

    private static void ShouldBeBusy(HttpResponseMessage response, JsonNode? json)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, json?.ToJsonString());
        response.Headers.GetValues("Retry-After").ShouldBe(["1"]);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        json!["type"]!.GetValue<string>().ShouldBe("urn:fluentcms:error:FCMS2411");
        json["code"]!.GetValue<string>().ShouldBe("FCMS2411");
        json["title"]!.GetValue<string>().ShouldBe("Entry busy");
        json["status"]!.GetValue<int>().ShouldBe(503);
        json["detail"]!.GetValue<string>().ShouldContain("try it again");
    }

    [Fact]
    public async Task AWriteThatWaitsPastTheLockTimeoutAnswers503WithRetryAfterAndChangesNothing()
    {
        var (id, publish) = await HeldPublishAsync();

        var (patched, json) = await SendAsync(HttpMethod.Patch, $"page/{id}", """{ "title": "Lost" }""", "\"1\"");

        ShouldBeBusy(patched, json);
        _held.Release.TrySetResult();
        (await publish).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (read, entry) = await SendAsync(HttpMethod.Get, $"page/{id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (entry!["title"]!.GetValue<string>(), entry["version"]!.GetValue<int>(), entry["status"]!.GetValue<string>()).ShouldBe(("Hello", 1, "published"));

        var (retried, again) = await SendAsync(HttpMethod.Patch, $"page/{id}", """{ "title": "Lost" }""", "\"1\"");
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, again?.ToJsonString());
        again!["version"]!.GetValue<int>().ShouldBe(2);
    }

    private sealed class HeldPublish
    {
        public bool Armed { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HoldingHook(HeldPublish held) : IAfterPublishHook<ContentDocument>
    {
        public async ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (held.Armed)
            {
                held.Armed = false;
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
