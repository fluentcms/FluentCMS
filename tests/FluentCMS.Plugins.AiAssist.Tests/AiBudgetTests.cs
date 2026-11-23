using System.Data.Common;
using System.Net;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The daily token budget: the tokens of a site's usage rows since midnight UTC, each call of the scripted client 17,
// against the lower of the site's dailyTokenBudget and the ceiling of the configuration. Each test has a host of its own,
// since one moves the clock a day and the others set the configuration or the instance defaults.
public sealed class AiBudgetTests
{
    private static readonly object Body = new { type = "article", field = "summary", text = "Ships are big boats." };

    // A call is let through while the site is under its budget, so the call that passes it is answered, and the next is
    // refused until the day ends, before its draft is read. Another site has a budget of its own.
    [Fact]
    public async Task ASiteOverItsDailyBudgetIsRefusedUntilTheBudgetResets()
    {
        await using var ai = new AiHost();
        var site = await ai.SiteAsync(settings => settings.DailyTokenBudget = 30);
        var other = await ai.SiteAsync(settings => settings.DailyTokenBudget = 30);
        using var admin = await ai.AdminAsync(site);
        using var colleague = await ai.AdminAsync(site);
        using var elsewhere = await ai.AdminAsync(other);

        await SuggestAsync(admin, "rewrite", Body);
        await SuggestAsync(admin, "rewrite", Body);
        using var over = await ActAsync(colleague, "rewrite", Body);
        using var draft = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", documentId = Guid.NewGuid() });
        await SuggestAsync(elsewhere, "rewrite", Body);
        var problem = await JsonAsync(over, HttpStatusCode.TooManyRequests);

        var now = ai.Host.Time.GetUtcNow();
        var tomorrow = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        problem["code"]!.GetValue<string>().ShouldBe("FCMS1693");
        problem["detail"]!.GetValue<string>().ShouldContain("30 AI tokens");
        problem["detail"]!.GetValue<string>().ShouldContain($"resets at {tomorrow:yyyy-MM-dd}T00:00:00Z");
        (await RefusedAsync(draft, HttpStatusCode.TooManyRequests)).ShouldBe("FCMS1693");
        ai.Chat.Calls.Count.ShouldBe(3);
        (await ai.UsageAsync(site)).Count.ShouldBe(2);

        // Logged once a day for the site, with the budget, what it used and when it resets.
        var logged = ai.Logs.Of(9369).Where(line => line.Contains(site.ToString(), StringComparison.Ordinal)).ShouldHaveSingleItem();
        logged.ShouldContain("34 of its daily budget of 30");
        logged.ShouldContain($"{tomorrow:yyyy-MM-dd}T00:00:00Z");

        ai.Host.Time.Advance(tomorrow - now);
        await SuggestAsync(colleague, "rewrite", Body);
    }

    // FluentCms:Ai:DailyTokenBudget is the operator's: it holds every call that is not known to go with a credential of the
    // site's own, which only the factory can tell. A key in the site's settings does not tell it, since a host's own
    // factory, such as one that signs in with an identity, may not send it. A site may set a lower budget, and a higher one
    // or none leaves the ceiling. A call the factory says goes with the site's own credential keeps the site's budget alone.
    [Fact]
    public async Task TheCeilingOfTheConfigurationHoldsEveryCallThatIsNotKnownToBeTheSites()
    {
        await using var ai = new AiHost(scripted: true, options => options.DailyTokenBudget = 30);
        var none = await ai.SiteAsync();
        var higher = await ai.SiteAsync(settings => settings.DailyTokenBudget = 1000);
        var lower = await ai.SiteAsync(settings => settings.DailyTokenBudget = 10);
        var keyInTheSettings = await ai.SiteAsync(settings => settings.ApiKey = "key-of-the-site");
        var own = await ai.SiteAsync();
        var ownAndBudget = await ai.SiteAsync(settings => settings.DailyTokenBudget = 10);

        (await CallsUntilRefusedAsync(ai, none)).ShouldBe((2, "30 AI tokens"));
        (await CallsUntilRefusedAsync(ai, higher)).ShouldBe((2, "30 AI tokens"));
        (await CallsUntilRefusedAsync(ai, lower)).ShouldBe((1, "10 AI tokens"));
        (await CallsUntilRefusedAsync(ai, keyInTheSettings)).ShouldBe((2, "30 AI tokens"));
        ai.Chat.SiteCredential = true;
        (await CallsUntilRefusedAsync(ai, own, most: 4)).ShouldBe((4, null));
        (await CallsUntilRefusedAsync(ai, ownAndBudget)).ShouldBe((1, "10 AI tokens"));
    }

    // Calls sent together each reserve their estimate, the text they send at about four characters a token and the
    // maxOutputTokens of the site, so the budget holds them before any has a row: here the first holds more than the
    // budget, and every other is refused while it is in flight. A call with the operator's credential that is cancelled
    // before the provider reports its usage is counted at that estimate.
    [Fact]
    public async Task CallsInFlightAreCountedAgainstTheBudgetAndACancelledOneAtItsEstimate()
    {
        await using var ai = new AiHost(scripted: true, options => options.DailyTokenBudget = 300);
        var site = await ai.SiteAsync(settings => settings.MaxOutputTokens = 200);
        using var admin = await ai.AdminAsync(site);
        ai.Chat.Hang = true;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var first = ActAsync(admin, "rewrite", Body, cancellationToken: caller.Token);
        await ai.Chat.Entered.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var others = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ActAsync(admin, "rewrite", Body)));
        foreach (var refused in others)
        {
            var problem = await JsonAsync(refused, HttpStatusCode.TooManyRequests);
            problem["code"]!.GetValue<string>().ShouldBe("FCMS1693");
            problem["detail"]!.GetValue<string>().ShouldContain("in flight");
            refused.Dispose();
        }

        var written = ai.Logs.NextAsync(9360);
        await caller.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(30), Token));
        await written.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var row = (await ai.UsageAsync(site)).ShouldHaveSingleItem();
        row.Succeeded.ShouldBeFalse();
        row.PromptTokens.ShouldBeGreaterThan(0);
        row.CompletionTokens.ShouldBe(200);
        ai.Chat.Calls.Count.ShouldBe(1);
        ai.Chat.Hang = false;
        using var after = await ActAsync(admin, "rewrite", Body);
        (await RefusedAsync(after, HttpStatusCode.TooManyRequests)).ShouldBe("FCMS1693");
    }

    // A stream whose provider reports the prompt first and the completion last, and that ends before the last part, at
    // the site's requestTimeout: the caller got the text, and the row counts the completion at maxOutputTokens.
    [Fact]
    public async Task AStreamThatEndsBeforeItsUsageIsCountedAtItsEstimate()
    {
        await using var ai = new AiHost(scripted: true, options => options.DailyTokenBudget = 260);
        var site = await ai.SiteAsync(settings => (settings.MaxOutputTokens, settings.RequestTimeout) = (250, 1));
        using var admin = await ai.AdminAsync(site);
        ai.Chat.Pieces = ["The whole suggestion."];
        ai.Chat.Hang = true;

        using var response = await ActAsync(admin, "expand", Body, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));
        ai.Chat.Hang = false;
        using var after = await ActAsync(admin, "rewrite", Body);

        events.Select(sent => sent.Event).ShouldBe(["delta", "error"]);
        var row = (await ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.PromptTokens, row.CompletionTokens).ShouldBe((12, 250));
        (await RefusedAsync(after, HttpStatusCode.TooManyRequests)).ShouldBe("FCMS1693");
    }

    // A prompt is never empty and an answer with text used tokens, so a provider or a gateway that reports 0 for them has
    // reported nothing, and a call with the operator's credential is counted at its estimate: a token for each UTF-8 byte
    // of what it sent, so a text in a script of several bytes a character is not counted short.
    [Fact]
    public async Task AUsageOfZeroForACallThatAnsweredIsCountedAtItsEstimate()
    {
        await using var ai = new AiHost();
        var site = await ai.SiteAsync(settings => settings.MaxOutputTokens = 300);
        using var admin = await ai.AdminAsync(site);
        (ai.Chat.PromptTokens, ai.Chat.CompletionTokens) = (0, 0);
        const string Text = "船は大きな舟です。🚢";

        var answer = await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = Text });
        await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = new string('x', Text.Length) });
        ai.Chat.SiteCredential = true;
        await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = Text });
        (ai.Chat.PromptTokens, ai.Chat.CompletionTokens) = (-5, -5);
        var negative = await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = Text });

        (answer["usage"]!["promptTokens"]!.GetValue<long>(), answer["usage"]!["completionTokens"]!.GetValue<long>()).ShouldBe((0, 0));
        (negative["usage"]!["promptTokens"]!.GetValue<long>(), negative["usage"]!["completionTokens"]!.GetValue<long>()).ShouldBe((0, 0));
        var rows = await ai.UsageAsync(site);

        // The same instructions and as many characters, so the rows differ by the bytes the text has beyond one a character.
        (rows[0].PromptTokens - rows[1].PromptTokens).ShouldBe(System.Text.Encoding.UTF8.GetByteCount(Text) - Text.Length);
        rows[0].CompletionTokens.ShouldBe(300);
        (rows[2].PromptTokens, rows[2].CompletionTokens).ShouldBe((0, 0));
        (rows[3].PromptTokens, rows[3].CompletionTokens).ShouldBe((0, 0));
    }

    // A usage row is written within a bound of its own. One that fails or does not end in time is a row not written
    // (event 9365): the answer is given, the reservation let go, and the call still counts for its day, so the refusal it
    // causes says the site has used its budget.
    [Fact]
    public async Task AUsageWriteThatFailsOrHangsIsNotWrittenAndStillCounts()
    {
        var writes = new UsageWrites();
        await using var ai = new AiHost();
        ai.Host.WithServices(services => services.ConfigureDbContext<PluginDbContext<AiAssistPlugin>>(options => options.AddInterceptors(writes)));
        var site = await ai.SiteAsync(settings => settings.DailyTokenBudget = 30);
        var other = await ai.SiteAsync(settings => settings.DailyTokenBudget = 1000);
        using var admin = await ai.AdminAsync(site);
        using var elsewhere = await ai.AdminAsync(other);

        writes.Fail = true;
        await SuggestAsync(admin, "rewrite", Body);
        await SuggestAsync(admin, "rewrite", Body);
        using var over = await ActAsync(admin, "rewrite", Body);
        writes.Fail = false;

        writes.Hang = true;
        var hanging = SuggestAsync(elsewhere, "rewrite", Body);
        await writes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        ai.Host.Time.Advance(AiAssistService.UsageWriteTimeout + TimeSpan.FromSeconds(1));
        await hanging.WaitAsync(TimeSpan.FromSeconds(30), Token);
        writes.Hang = false;
        await SuggestAsync(elsewhere, "rewrite", Body);

        var problem = await JsonAsync(over, HttpStatusCode.TooManyRequests);
        problem["code"]!.GetValue<string>().ShouldBe("FCMS1693");
        problem["detail"]!.GetValue<string>().ShouldContain("has used its daily budget");
        (await ai.UsageAsync(site)).ShouldBeEmpty();
        (await ai.UsageAsync(other)).Count.ShouldBe(1);
        ai.Logs.Of(9365).Count.ShouldBe(3);
    }

    // #3449: altText with no text and no draft sends its instructions and the image alone, so it reserves that and the
    // maxOutputTokens, not MaxInputLength characters of a draft it does not read. Its reservation is held here while the
    // usage of the day is read, and a call sent meanwhile fits the budget beside it; at 60,000 tokens more it did not.
    [Fact]
    public async Task AltTextWithNoTextAndNoDraftReservesWhatItSends()
    {
        var reads = new UsageReads();
        await using var ai = new AiHost();
        ai.Host.WithServices(services => services.ConfigureDbContext<PluginDbContext<AiAssistPlugin>>(options => options.AddInterceptors(reads)));
        var site = await ai.SiteAsync(settings => (settings.DailyTokenBudget, settings.AssetAltText) = (50_000, true));
        using var admin = await ai.AdminAsync(site);
        var image = await UploadAsync(admin, "harbour.png", Picture(40, 20));

        reads.Hold = true;
        var asset = ActAsync(admin, "altText", new { options = new { mediaId = image } });
        await reads.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        using var meanwhile = await ActAsync(admin, "rewrite", Body);
        reads.Release.TrySetResult();
        using var described = await asset.WaitAsync(TimeSpan.FromSeconds(30), Token);

        (await JsonAsync(meanwhile))["suggestion"]!.GetValue<string>().ShouldBe("A suggestion.");
        (await JsonAsync(described))["suggestion"]!.GetValue<string>().ShouldBe("A suggestion.");
        ai.Chat.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public void AnEstimateIsATokenForEachUtf8ByteAndARowThatCouldNotBeWrittenStillCountsForItsDay()
    {
        AiBudgets.Tokens("Ships").ShouldBe(5);
        AiBudgets.Tokens("船").ShouldBe(3);
        AiBudgets.Tokens("🚢").ShouldBe(4);
        AiBudgets.Tokens(null).ShouldBe(0);

        var budgets = new AiBudgets();
        var site = new SiteId(Guid.CreateVersion7());
        var day = new DateOnly(2026, 11, 19);
        budgets.Unrecorded(site, day, 100);
        budgets.Unrecorded(site, day, 20);
        var first = budgets.Reserve(site, day, 7);
        var second = budgets.Reserve(site, day, 5);
        first.Release();
        var nextDay = budgets.Reserve(site, day.AddDays(1), 1);

        first.Before.ShouldBe(120);
        second.Before.ShouldBe(127);
        nextDay.Before.ShouldBe(5);
    }

    // A call with the site's own credential keeps what its provider reported, 0 when it reported nothing.
    [Fact]
    public async Task ACallWithTheSitesOwnCredentialIsCountedAsReported()
    {
        await using var ai = new AiHost(scripted: true, options => options.DailyTokenBudget = 300);
        var site = await ai.SiteAsync(settings => (settings.MaxOutputTokens, settings.RequestTimeout) = (250, 1));
        using var admin = await ai.AdminAsync(site);
        ai.Chat.SiteCredential = true;
        ai.Chat.Pieces = ["The whole suggestion."];
        ai.Chat.Hang = true;

        using var response = await ActAsync(admin, "expand", Body, stream: true);
        await response.Content.ReadAsStringAsync(Token);

        var row = (await ai.UsageAsync(site)).ShouldHaveSingleItem();
        (row.PromptTokens, row.CompletionTokens).ShouldBe((12, 0));
    }

    // Settings are a site's whole, its secrets apart (4.13): a site with no settings of its own follows the budget of the
    // instance defaults, and one that saves its own settings has the budget it saved. Only the configuration's ceiling
    // holds it then.
    [Fact]
    public async Task TheBudgetOfTheInstanceDefaultsHoldsOnlyTheSitesThatFollowTheDefaults()
    {
        await using var ai = new AiHost();
        await ai.SetDefaultsAsync(new AiSettings { Provider = AiProvider.OpenAi, Model = "test-model", DailyTokenBudget = 30, Fields = { ["article"] = ["summary"] } });
        var following = await ai.BareSiteAsync();
        var saved = await ai.BareSiteAsync();
        await ai.SetSettingsAsync(saved, new AiSettings { Provider = AiProvider.OpenAi, Model = "test-model", Fields = { ["article"] = ["summary"] } });

        (await CallsUntilRefusedAsync(ai, following)).ShouldBe((2, "30 AI tokens"));
        (await CallsUntilRefusedAsync(ai, saved, most: 3)).ShouldBe((3, null));
    }

    [Fact]
    public void ACeilingBelowZeroStopsTheHost() =>
        new AiOptions { DailyTokenBudget = -1 }.IsValid().ShouldBeFalse();

    // The calls answered before the first refusal, at most the number given, and the budget the refusal names.
    private static async Task<(int Answered, string? Refusal)> CallsUntilRefusedAsync(AiHost ai, Guid site, int most = 10)
    {
        using var admin = await ai.AdminAsync(site);
        for (var answered = 0; answered < most; answered++)
        {
            using var response = await ActAsync(admin, "rewrite", Body);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var problem = await JsonAsync(response, HttpStatusCode.TooManyRequests);
                problem["code"]!.GetValue<string>().ShouldBe("FCMS1693");
                var detail = problem["detail"]!.GetValue<string>();
                const string Budget = "daily budget of ";
                var start = detail.IndexOf(Budget, StringComparison.Ordinal) + Budget.Length;
                return (answered, detail[start..detail.IndexOf('.', start)]);
            }
        }

        return (most, null);
    }

    // Holds the first read of the day's usage once Hold is set, which a budgeted call makes after it has reserved.
    private sealed class UsageReads : DbCommandInterceptor
    {
        public volatile bool Hold;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await HoldAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            await HoldAsync(command, cancellationToken);
            return result;
        }

        private async Task HoldAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (Hold && command.CommandText.Contains("SUM(", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains(AiUsageModel.UsageTable, StringComparison.Ordinal))
            {
                Hold = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class UsageWrites : SaveChangesInterceptor
    {
        public volatile bool Fail;
        public volatile bool Hang;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AiUsage>().Any() == true)
            {
                if (Fail)
                {
                    throw new InvalidOperationException("The usage table is gone.");
                }

                if (Hang)
                {
                    Entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }

            return result;
        }
    }
}
