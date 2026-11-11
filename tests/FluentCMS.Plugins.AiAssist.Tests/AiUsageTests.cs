using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// cms_ai_usage: one row for every call that is sent to a provider, and GET /ai/usage over the rows of a site.
public sealed class AiUsageTests : IClassFixture<AiHost>
{
    // In the text, the suggestion and the provider's failure of every call here, and in no row or log line after them.
    private const string Marker = "ships-that-must-not-be-kept";

    private const int CalledEvent = 9360;

    private readonly AiHost _ai;

    public AiUsageTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task EveryCallWritesOneRowWithItsCallerItsProviderWhatItUsedAndHowItEnded()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var body = new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." };
        _ai.Chat.Answer = $"Ships are large vessels, {Marker}.";
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (40, 9);

        var answered = await SuggestAsync(admin, "rewrite", body);
        _ai.Chat.Failure = new InvalidOperationException($"The provider could not read: {Marker}.");
        using var failed = await ActAsync(admin, "shorten", body);
        var problem = await JsonAsync(failed, HttpStatusCode.BadGateway);

        problem["code"]!.GetValue<string>().ShouldBe("FCMS1689");
        problem.ToJsonString().ShouldNotContain(Marker);
        var rows = await _ai.UsageAsync(site);
        rows.Count.ShouldBe(2);
        var row = rows[0];
        row.PrincipalKind.ShouldBe("apiKey");
        row.PrincipalId.ShouldBe(_ai.PrincipalOf(admin));
        (row.Action, row.Type, row.Field).ShouldBe(("rewrite", "article", "summary"));
        (row.Provider, row.Model).ShouldBe((ScriptedFactory.Provider, "test-model"));
        (row.PromptTokens, row.CompletionTokens, row.Succeeded).ShouldBe((40, 9, true));
        row.DurationMs.ShouldBe(answered["usage"]!["durationMs"]!.GetValue<long>());
        row.CreatedAt.ShouldBe(_ai.Host.Time.GetUtcNow());
        (rows[1].Action, rows[1].Succeeded, rows[1].PromptTokens, rows[1].CompletionTokens).ShouldBe(("shorten", false, 0, 0));

        // Neither the rows nor the log hold the text, the suggestion or what the provider said.
        foreach (var kept in rows)
        {
            string.Join(' ', kept.PrincipalKind, kept.Action, kept.Type, kept.Field, kept.Provider, kept.Model).ShouldNotContain(Marker);
        }

        _ai.Logs.Of(CalledEvent).Count(line => line.Contains(site.ToString(), StringComparison.Ordinal)).ShouldBe(2);
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACallWhoseCallerGoesAwayIsCancelledAndWritesItsRow()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Hang = true;
        var recorded = _ai.Logs.NextAsync(CalledEvent);
        using var gone = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var call = ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." }, cancellationToken: gone.Token);
        await _ai.Chat.Entered.WaitAsync(Token);
        await gone.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);
        await recorded.WaitAsync(Token);

        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.Action, row.Succeeded).ShouldBe(("rewrite", false));
        _ai.Logs.Of(CalledEvent).ShouldContain(line => line.Contains(site.ToString(), StringComparison.Ordinal) && line.Contains("cancelled", StringComparison.Ordinal));
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStreamWhoseCallerGoesAwayIsCancelledAndWritesItsRow()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["Ships are ", "large vessels."];
        _ai.Chat.Hang = true;
        var recorded = _ai.Logs.NextAsync(CalledEvent);

        using (var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true))
        {
            await using var events = await response.Content.ReadAsStreamAsync(Token);
            using var reader = new StreamReader(events);
            (await reader.ReadLineAsync(Token)).ShouldBe("event: delta");
        }

        await recorded.WaitAsync(Token);

        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        row.Succeeded.ShouldBeFalse();
        _ai.Logs.Of(CalledEvent).ShouldContain(line => line.Contains(site.ToString(), StringComparison.Ordinal) && line.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheUsageOfASiteNeedsItsPermissionAndIsFilteredByPrincipalActionAndTime()
    {
        var site = await _ai.SiteAsync();
        var elsewhere = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        using var editor = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null), ("content.article.update", null));
        using var auditor = await _ai.CallerAsync(site, (AiPermissions.ReadUsage, null));
        using var other = await _ai.AdminAsync(elsewhere);
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (10, 3);

        await SuggestAsync(admin, "rewrite", body);
        await SuggestAsync(editor, "rewrite", body);
        _ai.Host.Time.Advance(TimeSpan.FromHours(1));
        var later = _ai.Host.Time.GetUtcNow();
        await SuggestAsync(editor, "shorten", body);
        _ai.Chat.Failure = new InvalidOperationException("The provider broke.");
        (await ActAsync(editor, "shorten", body)).Dispose();
        _ai.Chat.Failure = null;
        await SuggestAsync(other, "rewrite", body);

        var all = await UsageAsync(auditor, "");
        var ofEditor = await UsageAsync(auditor, $"?principalId={_ai.PrincipalOf(editor)}");
        var shortened = await UsageAsync(auditor, "?action=shorten");
        var since = await UsageAsync(auditor, $"?from={Uri.EscapeDataString(later.ToString("O"))}");
        var sinceElsewhere = await UsageAsync(auditor, $"?from={Uri.EscapeDataString(later.ToOffset(TimeSpan.FromHours(5.5)).ToString("O"))}");
        var before = await UsageAsync(auditor, $"?to={Uri.EscapeDataString(later.ToString("O"))}");
        var paged = await UsageAsync(auditor, "?page=2&pageSize=3");

        // The rows of the site alone, the newest first, with the totals of every row the filter matches.
        all["total"]!.GetValue<long>().ShouldBe(4);
        (all["page"]!.GetValue<int>(), all["pageSize"]!.GetValue<int>()).ShouldBe((1, 50));
        var items = all["items"]!.AsArray();
        items.Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["shorten", "shorten", "rewrite", "rewrite"]);
        items.Select(item => item!["succeeded"]!.GetValue<bool>()).ShouldBe([false, true, true, true]);
        var newest = items[1]!;
        newest["principalKind"]!.GetValue<string>().ShouldBe("apiKey");
        newest["principalId"]!.GetValue<Guid>().ShouldBe(_ai.PrincipalOf(editor));
        (newest["type"]!.GetValue<string>(), newest["field"]!.GetValue<string>()).ShouldBe(("article", "summary"));
        (newest["provider"]!.GetValue<string>(), newest["model"]!.GetValue<string>()).ShouldBe((ScriptedFactory.Provider, "test-model"));
        newest["createdAt"]!.GetValue<DateTimeOffset>().ShouldBe(later);
        Totals(all).ShouldBe((4, 30, 9));

        ofEditor["total"]!.GetValue<long>().ShouldBe(3);
        Totals(ofEditor).ShouldBe((3, 20, 6));
        shortened["total"]!.GetValue<long>().ShouldBe(2);
        Totals(shortened).ShouldBe((2, 10, 3));
        since["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["shorten", "shorten"]);
        sinceElsewhere["total"]!.GetValue<long>().ShouldBe(2);
        before["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["rewrite", "rewrite"]);
        paged["items"]!.AsArray().ShouldHaveSingleItem()!["action"]!.GetValue<string>().ShouldBe("rewrite");
        Totals(paged).ShouldBe((4, 30, 9));

        // The caller who may run actions may not read the log, and a parameter that cannot be read is refused.
        using var refused = await editor.GetAsync(new Uri(Usage, UriKind.Relative), Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var query in new[] { "?principalId=someone", "?action=paraphrase", "?from=2026-11-01T00:00:00", "?to=yesterday", "?page=0", "?pageSize=101", "?page=2147483647", "?page=42949674&pageSize=100" })
        {
            using var invalid = await auditor.GetAsync(new Uri(Usage + query, UriKind.Relative), Token);
            (await RefusedAsync(invalid, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680", query);
        }
    }

    private static async Task<JsonNode> UsageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri(Usage + query, UriKind.Relative), Token);
        return await JsonAsync(response);
    }

    private static (long Calls, long PromptTokens, long CompletionTokens) Totals(JsonNode page) =>
        (page["totals"]!["calls"]!.GetValue<long>(), page["totals"]!["promptTokens"]!.GetValue<long>(), page["totals"]!["completionTokens"]!.GetValue<long>());
}
