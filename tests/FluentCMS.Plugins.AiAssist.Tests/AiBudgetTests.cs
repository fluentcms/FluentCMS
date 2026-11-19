using System.Net;
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

    // FluentCms:Ai:DailyTokenBudget is the operator's: it holds every site that calls with the operator's key, which is a
    // site with no key of its own. Such a site may set a lower budget, and a higher one or none leaves the ceiling. A site
    // with a key of its own pays its own calls and keeps its own budget alone.
    [Fact]
    public async Task TheCeilingOfTheConfigurationHoldsEverySiteWithoutAKeyOfItsOwn()
    {
        await using var ai = new AiHost(scripted: true, options => options.DailyTokenBudget = 30);
        var none = await ai.SiteAsync();
        var higher = await ai.SiteAsync(settings => settings.DailyTokenBudget = 1000);
        var lower = await ai.SiteAsync(settings => settings.DailyTokenBudget = 10);
        var ownKey = await ai.SiteAsync(settings => settings.ApiKey = "key-of-the-site");
        var ownKeyAndBudget = await ai.SiteAsync(settings => (settings.ApiKey, settings.DailyTokenBudget) = ("key-of-the-site", 10));

        (await CallsUntilRefusedAsync(ai, none)).ShouldBe((2, "30 AI tokens"));
        (await CallsUntilRefusedAsync(ai, higher)).ShouldBe((2, "30 AI tokens"));
        (await CallsUntilRefusedAsync(ai, lower)).ShouldBe((1, "10 AI tokens"));
        (await CallsUntilRefusedAsync(ai, ownKey, most: 4)).ShouldBe((4, null));
        (await CallsUntilRefusedAsync(ai, ownKeyAndBudget)).ShouldBe((1, "10 AI tokens"));
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
}
