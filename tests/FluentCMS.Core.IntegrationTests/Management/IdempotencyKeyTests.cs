using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// A create whose title is "Hold" waits in its hook until the test lets it go, so a repeat can arrive while it runs.
public sealed class HeldCreates
{
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reset()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class IdempotencyFixture : IAsyncLifetime
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public HeldCreates Held { get; } = new();

    public FluentCmsTestHost Host { get; }

    public IdempotencyFixture() =>
        Host = new FluentCmsTestHost()
            .WithContentTypes<Page, Person>()
            .Configure(cms =>
            {
                cms.Idempotency.Window = Window;
                cms.Idempotency.InProgressTimeout = TimeSpan.FromMinutes(1);
            })
            .WithServices(services =>
            {
                services.AddSingleton(Held);
                services.AddScoped<IBeforeCreateHook<ContentDocument>, HoldingHook>();
            });

    public HttpClient Editor { get; private set; } = null!;

    public HttpClient OtherEditor { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        Editor = Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: token));
        OtherEditor = Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: token));
    }

    public async ValueTask DisposeAsync()
    {
        Editor.Dispose();
        OtherEditor.Dispose();
        await Host.DisposeAsync();
    }

    private sealed class HoldingHook(HeldCreates held) : IBeforeCreateHook<ContentDocument>
    {
        public async ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (context.Entry.TryGetValue("title", out var title) && title?.ToString() == "Hold")
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }
}

// The acceptance of #2933 over HTTP: an Idempotency-Key on a create and on a bulk request.
public sealed class IdempotencyKeyTests(IdempotencyFixture fixture) : IClassFixture<IdempotencyFixture>
{
    private static async Task<(HttpStatusCode Status, JsonNode? Json, HttpResponseMessage Response)> SendAsync(HttpClient client, string path, string json, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ManagementFixture.Root + path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response);
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private async Task<int> CountAsync(string section)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{ManagementFixture.Root}page?filter[section][eq]={section}", UriKind.Relative));
        using var response = await fixture.Editor.SendAsync(request, TestContext.Current.CancellationToken);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        return json["items"]!.AsArray().Count;
    }

    private static string Page(string title, string section) => new JsonObject { ["title"] = title, ["section"] = section }.ToJsonString();

    private static string Section() => $"s-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task ARetriedCreateWithTheSameKeyAnswersTheFirstResponseAndCreatesNothingNew()
    {
        var key = NewKey();
        var section = Section();

        var first = await SendAsync(fixture.Editor, "page", Page("Once", section), key);
        var repeat = await SendAsync(fixture.Editor, "page", Page("Once", section), key);

        first.Status.ShouldBe(HttpStatusCode.Created);
        first.Response.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        repeat.Status.ShouldBe(HttpStatusCode.Created);
        repeat.Json!.ToJsonString().ShouldBe(first.Json!.ToJsonString());
        repeat.Response.Headers.Location.ShouldBe(first.Response.Headers.Location);
        repeat.Response.Headers.ETag!.ToString().ShouldBe("\"1\"");
        repeat.Response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        repeat.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await CountAsync(section)).ShouldBe(1);
    }

    [Fact]
    public async Task WithoutAKeyEveryCreateRuns()
    {
        var section = Section();

        await SendAsync(fixture.Editor, "page", Page("Twice", section), key: null);
        await SendAsync(fixture.Editor, "page", Page("Twice", section), key: null);

        (await CountAsync(section)).ShouldBe(2);
    }

    [Fact]
    public async Task TheSameKeyWithAnotherBodyIsRefused()
    {
        var key = NewKey();
        var section = Section();
        (await SendAsync(fixture.Editor, "page", Page("First", section), key)).Status.ShouldBe(HttpStatusCode.Created);

        var other = await SendAsync(fixture.Editor, "page", Page("Second", section), key);
        var elsewhere = await SendAsync(fixture.Editor, "person", """{ "name": "First" }""", key);

        other.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        other.Json!["code"]!.GetValue<string>().ShouldBe("FCMS3141");
        other.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("Idempotency-Key");
        elsewhere.Json!["code"]!.GetValue<string>().ShouldBe("FCMS3141");
        (await CountAsync(section)).ShouldBe(1);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstStillRunsAnswers409()
    {
        var key = NewKey();
        var section = Section();
        fixture.Held.Reset();

        var first = SendAsync(fixture.Editor, "page", Page("Hold", section), key);
        await fixture.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var during = await SendAsync(fixture.Editor, "page", Page("Hold", section), key);
        fixture.Held.Release.SetResult();
        var answered = await first;
        var after = await SendAsync(fixture.Editor, "page", Page("Hold", section), key);

        during.Status.ShouldBe(HttpStatusCode.Conflict);
        during.Json!["code"]!.GetValue<string>().ShouldBe("FCMS3142");
        answered.Status.ShouldBe(HttpStatusCode.Created);
        after.Status.ShouldBe(HttpStatusCode.Created);
        after.Json!["id"]!.GetValue<string>().ShouldBe(answered.Json!["id"]!.GetValue<string>());
        (await CountAsync(section)).ShouldBe(1);
    }

    [Fact]
    public async Task TwoPrincipalsNeverShareAKey()
    {
        var key = NewKey();
        var section = Section();

        var mine = await SendAsync(fixture.Editor, "page", Page("Shared key", section), key);
        var theirs = await SendAsync(fixture.OtherEditor, "page", Page("Shared key", section), key);

        theirs.Status.ShouldBe(HttpStatusCode.Created);
        theirs.Response.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        theirs.Json!["id"]!.GetValue<string>().ShouldNotBe(mine.Json!["id"]!.GetValue<string>());
        (await CountAsync(section)).ShouldBe(2);
    }

    [Fact]
    public async Task ARefusedCreateKeepsNothingSoACorrectedRetryRuns()
    {
        var key = NewKey();
        var section = Section();

        var refused = await SendAsync(fixture.Editor, "page", Page("Too long", new string('x', 51)), key);
        var corrected = await SendAsync(fixture.Editor, "page", Page("Too long", section), key);

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        corrected.Status.ShouldBe(HttpStatusCode.Created);
        (await CountAsync(section)).ShouldBe(1);
    }

    [Fact]
    public async Task ABulkRequestWithTheSameKeyRunsOnce()
    {
        var key = NewKey();
        var section = Section();
        var created = await SendAsync(fixture.Editor, "page", Page("To delete", section), key: null);
        var id = created.Json!["id"]!.GetValue<string>();
        var body = $$"""{ "operation": "delete", "items": [ { "id": "{{id}}" } ] }""";

        var first = await SendAsync(fixture.Editor, "page/bulk", body, key);
        var repeat = await SendAsync(fixture.Editor, "page/bulk", body, key);
        var unkeyed = await SendAsync(fixture.Editor, "page/bulk", body, key: null);

        first.Status.ShouldBe(HttpStatusCode.OK, first.Json?.ToJsonString());
        first.Json!["items"]![0]!["status"]!.GetValue<string>().ShouldBe("succeeded");
        repeat.Json!.ToJsonString().ShouldBe(first.Json.ToJsonString());
        repeat.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        unkeyed.Json!["items"]![0]!["status"]!.GetValue<string>().ShouldBe("failed");
    }

    [Fact]
    public async Task AKeyIsForgottenOnceTheWindowHasPassed()
    {
        var key = NewKey();
        var section = Section();
        await SendAsync(fixture.Editor, "page", Page("Expires", section), key);

        fixture.Host.Time.Advance(IdempotencyFixture.Window);
        var later = await SendAsync(fixture.Editor, "page", Page("Expires", section), key);

        later.Status.ShouldBe(HttpStatusCode.Created);
        later.Response.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await CountAsync(section)).ShouldBe(2);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("tab	inside")]
    public async Task AKeyThatIsNotVisibleAsciiIsABadRequest(string key)
    {
        var section = Section();

        var refused = await SendAsync(fixture.Editor, "page", Page("Bad key", section), key);

        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Json!["code"]!.GetValue<string>().ShouldBe("FCMS3140");
        (await CountAsync(section)).ShouldBe(0);
    }
}
