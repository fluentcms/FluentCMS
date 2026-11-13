using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shouldly;
using SkiaSharp;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The default factory against a local stand-in in the chat completions format of OpenAI: where a provider puts the usage,
// a provider that sends none, the image of altText, and a slow provider that passes the timeout or whose caller goes away.
// The host logs at Trace, and no line of it may hold the text or the suggestion of a call.
public sealed class AiWireTests : IAsyncLifetime
{
    private const string Text = "Ships are big boats, ships-of-the-text.";
    private const string TextMarker = "ships-of-the-text";
    private const string AnswerMarker = "vessels-of-the-answer";
    private const int CalledEvent = 9360;

    private StubProvider _stub = null!;
    private AiHost _ai = null!;

    public async ValueTask InitializeAsync()
    {
        _stub = await StubProvider.StartAsync();
        _stub.Answer = $"Ships are large {AnswerMarker}.";
        _ai = new AiHost(scripted: false, options => options.AllowedEndpoints.Add(_stub.Url("v1")), LogLevel.Trace);
    }

    public async ValueTask DisposeAsync()
    {
        await _ai.DisposeAsync();
        await _stub.DisposeAsync();
    }

    // As some compatible servers send it, with the last piece of the answer and no chunk of its own.
    [Fact]
    public async Task AStreamWhoseUsageArrivesOnlyWithTheLastPieceCarriesIt()
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _stub.Usage = StubUsage.LastChunk;

        var events = await StreamAsync(admin, "rewrite");

        events.Select(sent => sent.Event).ShouldBe(["delta", "delta", "done"]);
        events[^1].Data["suggestion"]!.GetValue<string>().ShouldBe(_stub.Answer);
        Tokens(events[^1].Data["usage"]!).ShouldBe((StubProvider.PromptTokens, StubProvider.CompletionTokens));
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((true, StubProvider.PromptTokens, StubProvider.CompletionTokens));
    }

    // The answer has ended, but the usage is still on its way: done waits for it.
    [Fact]
    public async Task AStreamWhoseUsageArrivesLateEndsOnceItHasArrived()
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _stub.UsageDelay = TimeSpan.FromMilliseconds(500);

        var events = await StreamAsync(admin, "rewrite");

        events.Select(sent => sent.Event).ShouldBe(["delta", "delta", "done"]);
        var usage = events[^1].Data["usage"]!;
        Tokens(usage).ShouldBe((StubProvider.PromptTokens, StubProvider.CompletionTokens));
        usage["durationMs"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(450);
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((true, StubProvider.PromptTokens, StubProvider.CompletionTokens));

        // The stream asks for its usage, which OpenAI sends only when asked, and the answer is capped with
        // max_completion_tokens rather than the older max_tokens, so the cap holds only where an endpoint reads it.
        var sent = _stub.Requests.ShouldHaveSingleItem().Body!;
        sent["stream_options"]!["include_usage"]!.GetValue<bool>().ShouldBeTrue();
        sent["max_completion_tokens"]!.GetValue<int>().ShouldBe(1024);
        sent["max_tokens"].ShouldBeNull();
    }

    // The image is a part of the user message, as a data URI of the reduced copy, the form OpenAI and Ollama's /v1 read.
    [Fact]
    public async Task AltTextSendsTheReducedCopyAsAnImagePartOfTheUserMessage()
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var image = await UploadAsync(admin, "harbour.png", Picture(2000, 1000));

        var answer = await SuggestAsync(admin, "altText", new { type = "article", field = "summary", options = new { mediaId = image } });

        answer["suggestion"]!.GetValue<string>().ShouldBe(_stub.Answer);
        var messages = _stub.Requests.ShouldHaveSingleItem().Body!["messages"]!.AsArray();
        messages.Select(message => message!["role"]!.GetValue<string>()).ShouldBe(["system", "user"]);
        var part = messages[1]!["content"]!.AsArray().ShouldHaveSingleItem()!;
        part["type"]!.GetValue<string>().ShouldBe("image_url");
        var url = part["image_url"]!["url"]!.GetValue<string>();
        url.ShouldStartWith("data:image/png;base64,");
        using var copy = SKBitmap.Decode(Convert.FromBase64String(url["data:image/png;base64,".Length..]));
        (copy.Width, copy.Height).ShouldBe((1024, 512));
    }

    // Nothing to count is not a failure: the suggestion is answered, and the row and the answer count no tokens.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAnswerWithoutUsageIsASuggestionThatCountsNoTokens(bool stream)
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _stub.Usage = StubUsage.None;

        var answer = stream ? (await StreamAsync(admin, "rewrite"))[^1].Data : await SuggestAsync(admin, "rewrite", Body());

        answer["suggestion"]!.GetValue<string>().ShouldBe(_stub.Answer);
        Tokens(answer["usage"]!).ShouldBe((0, 0));
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((true, 0, 0));
        _ai.Logs.Of(CalledEvent).ShouldHaveSingleItem().ShouldContain("succeeded");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACallThatPassesTheTimeoutOfASlowProviderWritesOneFailedRow(bool stream)
    {
        var site = await SiteAsync(timeout: 1);
        using var admin = await _ai.AdminAsync(site);
        _stub.Pause = TimeSpan.FromSeconds(30);

        if (stream)
        {
            var events = await StreamAsync(admin, "rewrite");
            events.Select(sent => sent.Event).ShouldBe(["delta", "error"]);
            events[^1].Data["code"]!.GetValue<string>().ShouldBe("FCMS1688");
            events[^1].Data["status"]!.GetValue<int>().ShouldBe(504);
        }
        else
        {
            using var response = await ActAsync(admin, "rewrite", Body());
            (await RefusedAsync(response, HttpStatusCode.GatewayTimeout)).ShouldBe("FCMS1688");
        }

        _stub.Requests.ShouldHaveSingleItem();
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((false, 0, 0));
        row.DurationMs.ShouldBeInRange(900, 10_000);
        _ai.Logs.Of(CalledEvent).ShouldHaveSingleItem().ShouldContain("timeout FCMS1688");
        _ai.Logs.Of(9361).ShouldBeEmpty();
        ShouldHoldNothingOfTheCall();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACallWhoseCallerGoesAwayFromASlowProviderWritesOneFailedRow(bool stream)
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _stub.Pause = TimeSpan.FromSeconds(30);
        var recorded = _ai.Logs.NextAsync(CalledEvent);

        if (stream)
        {
            // The first piece has arrived when the caller goes away.
            using var response = await ActAsync(admin, "rewrite", Body(), stream: true);
            await using var events = await response.Content.ReadAsStreamAsync(Token);
            using var reader = new StreamReader(events);
            (await reader.ReadLineAsync(Token)).ShouldBe("event: delta");
        }
        else
        {
            using var gone = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var received = _stub.Received;
            var call = ActAsync(admin, "rewrite", Body(), cancellationToken: gone.Token);
            await received.WaitAsync(Token);
            await gone.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => call);
        }

        await recorded.WaitAsync(TimeSpan.FromSeconds(10), Token);

        _stub.Requests.ShouldHaveSingleItem();
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Succeeded, row.PromptTokens, row.CompletionTokens).ShouldBe((false, 0, 0));
        row.DurationMs.ShouldBeLessThan(10_000);
        _ai.Logs.Of(CalledEvent).ShouldHaveSingleItem().ShouldContain("cancelled");
        _ai.Logs.Of(9361).ShouldBeEmpty();
        _ai.Logs.Of(9367).ShouldBeEmpty();
        ShouldHoldNothingOfTheCall();
    }

    // With the debug lines of the framework and the packages: the text and the suggestion of an answer, a stream, seo and
    // a provider that refuses with what it was sent are in no line and no row.
    [Fact]
    public async Task WithDebugLoggingNoLineOrRowHoldsTheTextOrTheSuggestion()
    {
        var site = await SiteAsync();
        using var admin = await _ai.AdminAsync(site);

        await SuggestAsync(admin, "rewrite", Body());
        await StreamAsync(admin, "shorten");
        _stub.Answer = $$"""{"metaTitle": "Ships", "metaDescription": "Large {{AnswerMarker}}."}""";
        (await SuggestAsync(admin, "seo", Body()))["suggestion"]!["metaDescription"]!.GetValue<string>().ShouldContain(AnswerMarker);
        _stub.Status = 400;
        using var refused = await ActAsync(admin, "rewrite", Body());
        (await RefusedAsync(refused, HttpStatusCode.BadGateway)).ShouldBe("FCMS1689");

        _ai.Logs.At(LogLevel.Debug).ShouldNotBeEmpty();
        _ai.Logs.Of(CalledEvent).Count.ShouldBe(4);
        var rows = await _ai.UsageAsync(site);
        rows.Select(row => row.Succeeded).ShouldBe([true, true, true, false]);
        foreach (var row in rows)
        {
            string.Join(' ', row.PrincipalKind, row.Action, row.Type, row.Field, row.Provider, row.Model).ShouldNotContain("ships-");
        }

        ShouldHoldNothingOfTheCall();
    }

    private async Task<Guid> SiteAsync(int timeout = 60) =>
        await _ai.SiteAsync(settings => (settings.Provider, settings.Model, settings.Endpoint, settings.ApiKey, settings.RequestTimeout) = (AiProvider.OpenAiCompatible, "a-model", _stub.Url("v1"), null, timeout));

    private static object Body() => new { type = "article", field = "summary", text = Text };

    private static async Task<List<(string Event, JsonNode Data)>> StreamAsync(HttpClient client, string action)
    {
        using var response = await ActAsync(client, action, Body(), stream: true);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return StreamEvents(await response.Content.ReadAsStringAsync(Token));
    }

    private static (long Prompt, long Completion) Tokens(JsonNode usage) =>
        (usage["promptTokens"]!.GetValue<long>(), usage["completionTokens"]!.GetValue<long>());

    private void ShouldHoldNothingOfTheCall() =>
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(TextMarker, StringComparison.Ordinal) || line.Contains(AnswerMarker, StringComparison.Ordinal));
}
