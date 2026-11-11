using System.Net;
using FluentCMS.Content;
using FluentCMS.Sites;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// With Accept: text/event-stream the answer is server-sent events.
public sealed class AiStreamTests : IClassFixture<AiHost>
{
    private readonly AiHost _ai;

    public AiStreamTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task TheSuggestionArrivesAsDeltaEventsAndEndsWithDoneWhichCarriesTheUsage()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["Ships are ", "large ", "vessels."];
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (31, 7);

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        events.Select(sent => sent.Event).ShouldBe(["delta", "delta", "delta", "done"]);
        events.Take(3).Select(sent => sent.Data["text"]!.GetValue<string>()).ShouldBe(["Ships are ", "large ", "vessels."]);
        var done = events[3].Data;
        done["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
        done["usage"]!["provider"]!.GetValue<string>().ShouldBe(ScriptedFactory.Provider);
        done["usage"]!["model"]!.GetValue<string>().ShouldBe("test-model");
        done["usage"]!["promptTokens"]!.GetValue<long>().ShouldBe(31);
        done["usage"]!["completionTokens"]!.GetValue<long>().ShouldBe(7);
        _ai.Chat.Calls.ShouldHaveSingleItem().Streamed.ShouldBeTrue();

        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((true, 31, 7));
        row.DurationMs.ShouldBe(done["usage"]!["durationMs"]!.GetValue<long>());
    }

    // The suggestion of seo is an object that exists only at the end, so it has no pieces to send.
    [Fact]
    public async Task SeoStreamsNoDeltaAndEndsWithItsObject()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["{\"metaTitle\": \"Ships", " at sea\", \"metaDescription\": \"Where ships sail.\"}"];

        using var response = await ActAsync(admin, "seo", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        var done = events.ShouldHaveSingleItem();
        done.Event.ShouldBe("done");
        done.Data["suggestion"]!["metaTitle"]!.GetValue<string>().ShouldBe("Ships at sea");
        done.Data["suggestion"]!["metaDescription"]!.GetValue<string>().ShouldBe("Where ships sail.");
    }

    [Fact]
    public async Task AProviderThatFailsWhileItStreamsEndsTheEventsWithTheProblem()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["Ships are ", "large vessels."];
        _ai.Chat.Failure = new InvalidOperationException("The provider broke.");

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        events.Select(sent => sent.Event).ShouldBe(["delta", "error"]);
        var problem = events[1].Data;
        problem["code"]!.GetValue<string>().ShouldBe("FCMS1689");
        problem["status"]!.GetValue<int>().ShouldBe(502);
        problem["type"]!.GetValue<string>().ShouldBe("urn:fluentcms:error:FCMS1689");
        problem["detail"]!.GetValue<string>().ShouldNotContain("broke");
        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().Succeeded.ShouldBeFalse();
    }

    // A write to a connection that is gone may fail with a cancellation of its own, before the request says it was
    // aborted. That is the caller going away: the call is cancelled, not a failure of the provider.
    [Fact]
    public async Task APieceThatCannotBeWrittenEndsTheCallAsCancelledWhateverTheWriteThrows()
    {
        var site = Guid.CreateVersion7();
        await using var scope = _ai.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AiAssistService>();
        var call = new AiCall(
            "rewrite",
            new SiteId(site),
            new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "editor"),
            "article",
            "summary",
            new AiClient(_ai.Chat, ScriptedFactory.Provider, "test-model"),
            [new ChatMessage(ChatRole.User, "Ships are big boats.")],
            new ChatOptions(),
            TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<OperationCanceledException>(() => service.RunAsync(call, (_, _) => throw new OperationCanceledException("The connection was aborted."), Token));
        await Should.ThrowAsync<OperationCanceledException>(() => service.RunAsync(call, (_, _) => throw new IOException("The connection was reset."), Token));

        (await _ai.UsageAsync(site)).Select(usage => usage.Succeeded).ShouldBe([false, false]);
        _ai.Logs.Of(9360).Count(line => line.Contains(site.ToString(), StringComparison.Ordinal) && line.Contains("cancelled", StringComparison.Ordinal)).ShouldBe(2);
        _ai.Logs.Of(9361).ShouldNotContain(line => line.Contains(site.ToString(), StringComparison.Ordinal));
    }

    // Nothing was sent to a provider, so there is no stream to end: the answer is the problem itself.
    [Fact]
    public async Task ARefusalBeforeTheProviderIsAskedIsAProblemAndNotAnEvent()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "notes", text = "Ships are big boats." }, stream: true);

        (await RefusedAsync(response, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        _ai.Chat.Calls.ShouldBeEmpty();
    }
}
