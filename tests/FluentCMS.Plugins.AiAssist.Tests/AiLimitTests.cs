using System.Net;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The checks a call passes before a provider is asked, and the limits that hold it.
public sealed class AiLimitTests : IClassFixture<AiHost>
{
    private readonly AiHost _ai;

    public AiLimitTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task ACallerWithoutThePermissionAndAFieldThatIsNotOptedInAreRefused()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        using var editor = await _ai.CallerAsync(site, ("content.article.read", null), ("content.article.update", null));
        using var anonymous = _ai.Host.CreateClient();
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };

        using var noPermission = await ActAsync(editor, "rewrite", body);
        using var notSignedIn = await ActAsync(anonymous, "rewrite", body);
        using var notOptedIn = await ActAsync(admin, "rewrite", new { type = "article", field = "notes", text = "Ships are big boats." });
        using var notText = await ActAsync(admin, "rewrite", new { type = "article", field = "rating", text = "Ships are big boats." });
        using var noSuchField = await ActAsync(admin, "rewrite", new { type = "article", field = "subtitle", text = "Ships are big boats." });
        using var noSuchType = await ActAsync(admin, "rewrite", new { type = "page", field = "summary", text = "Ships are big boats." });
        using var noField = await ActAsync(admin, "rewrite", new { type = "article", text = "Ships are big boats." });

        (await RefusedAsync(noPermission, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        notSignedIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefusedAsync(notOptedIn, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
        (await RefusedAsync(notText, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
        (await RefusedAsync(noSuchField, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
        (await RefusedAsync(noSuchType, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
        (await RefusedAsync(noField, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
        _ai.Chat.Calls.ShouldBeEmpty();
        (await _ai.UsageAsync(site)).ShouldBeEmpty();
    }

    // The permission comes first: a caller without it learns nothing of the fields a site opted in.
    [Fact]
    public async Task ThePermissionIsCheckedBeforeTheOptIn()
    {
        var site = await _ai.SiteAsync();
        using var editor = await _ai.CallerAsync(site, ("content.article.read", null), ("content.article.update", null));

        using var response = await ActAsync(editor, "rewrite", new { type = "article", field = "notes", text = "Ships are big boats." });

        (await RefusedAsync(response, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
    }

    [Fact]
    public async Task AnOptInThatASiteHasNotSavedLeavesAiAssistOff()
    {
        var site = await _ai.SiteAsync(settings => settings.Fields.Clear());
        using var admin = await _ai.AdminAsync(site);

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });

        (await RefusedAsync(response, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
    }

    [Fact]
    public async Task APrincipalMakesAtMostTheCallsPerMinuteOfTheSite()
    {
        var site = await _ai.SiteAsync(settings => settings.CallsPerMinute = 2);
        using var admin = await _ai.AdminAsync(site);
        using var colleague = await _ai.AdminAsync(site);
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };

        await SuggestAsync(admin, "rewrite", body);
        await SuggestAsync(admin, "rewrite", body);
        using var over = await ActAsync(admin, "rewrite", body);
        using var again = await ActAsync(admin, "rewrite", body);
        var problem = await JsonAsync(over, HttpStatusCode.TooManyRequests);

        problem["code"]!.GetValue<string>().ShouldBe("FCMS1687");
        problem["detail"]!.GetValue<string>().ShouldContain("2 AI calls a minute");
        (await RefusedAsync(again, HttpStatusCode.TooManyRequests)).ShouldBe("FCMS1687");

        // A refused call asks no provider, writes no row and takes nothing from the caller's allowance.
        _ai.Chat.Calls.Count.ShouldBe(2);
        (await _ai.UsageAsync(site)).Count.ShouldBe(2);
        _ai.Logs.Of(9364).Count(line => line.Contains(site.ToString(), StringComparison.Ordinal)).ShouldBe(1);

        // Another principal has an allowance of its own, and the caller's fills again with time.
        await SuggestAsync(colleague, "rewrite", body);
        _ai.Host.Time.Advance(TimeSpan.FromSeconds(31));
        await SuggestAsync(admin, "rewrite", body);
        using var stillOver = await ActAsync(admin, "rewrite", body);
        (await RefusedAsync(stillOver, HttpStatusCode.TooManyRequests)).ShouldBe("FCMS1687");
    }

    [Fact]
    public async Task ATextOverTheInputLimitIsRefused()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);

        using var atTheLimit = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = new string('a', 20_000) });
        using var over = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = new string('a', 20_001) });

        atTheLimit.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefusedAsync(over, HttpStatusCode.RequestEntityTooLarge)).ShouldBe("FCMS1686");
        _ai.Chat.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ACallThatPassesTheTimeoutOfTheSiteEndsWithAProblemAndAFailedRow()
    {
        var site = await _ai.SiteAsync(settings => settings.RequestTimeout = 1);
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Hang = true;

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        var problem = await JsonAsync(response, HttpStatusCode.GatewayTimeout);

        problem["code"]!.GetValue<string>().ShouldBe("FCMS1688");
        problem["detail"]!.GetValue<string>().ShouldContain("1 seconds");
        var row = (await _ai.UsageAsync(site)).ShouldHaveSingleItem();
        row.Succeeded.ShouldBeFalse();
        row.DurationMs.ShouldBeGreaterThanOrEqualTo(900);
    }

    [Fact]
    public async Task AStreamThatPassesTheTimeoutEndsWithTheProblemAsAnEvent()
    {
        var site = await _ai.SiteAsync(settings => settings.RequestTimeout = 1);
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Pieces = ["Ships are ", "large vessels."];
        _ai.Chat.Hang = true;

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        events.Select(sent => sent.Event).ShouldBe(["delta", "error"]);
        events[1].Data["code"]!.GetValue<string>().ShouldBe("FCMS1688");
        events[1].Data["status"]!.GetValue<int>().ShouldBe(504);
        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().Succeeded.ShouldBeFalse();
    }
}
