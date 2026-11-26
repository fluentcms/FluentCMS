using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Administration.Account;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Account;

// The consents to MCP clients of the account page (#3592): listed from GET /mcp/consents of the MCP plugin while the shell has
// the plugin enabled in the site, each ended after a question, with a text of its own for each refusal of the end.
public sealed class AccountMcpConsentsTests : AccountTestContext
{
    private const string Heading = "MCP clients you allow";
    private const string Morning = "2026-11-16T09:30:00+00:00";
    private static readonly Guid Desktop = new("0193a1b2-0000-7000-8000-0000000000d1");
    private static readonly Guid Editor = new("0193a1b2-0000-7000-8000-0000000000d2");
    private static readonly Guid Other = new("0193a1b2-0000-7000-8000-00000000a002");

    private readonly JsonArray consents = [];

    public AccountMcpConsentsTests() => Management.Plugins[MainSite].Add(Plugin(AccountConsentsApi.Plugin, enabled: true));

    private string When(string moment) => Services.GetRequiredService<FormatService>().DateTime(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void TheClientsAreListedWithTheStateOfEachConsentAndTheOneEndedGoesAfterAQuestion()
    {
        AddConsent(Desktop, "Desktop agent", Morning, active: true);
        AddConsent(Editor, "Editor agent", Morning, active: false);
        Serve(end: id =>
        {
            consents.RemoveAt(0);
            return NoContent();
        });
        var app = Load(cards: 6);
        var section = Section(app, Heading);
        Names(section).ShouldBe(["Desktop agent", "Editor agent"]);
        section.TextContent.ShouldContain($"Your consent holds, given {When(Morning)}");
        section.TextContent.ShouldContain($"Your consent no longer holds, given {When(Morning)}; the client still counts");
        Management.Requests.ShouldContain(("mcp/consents", MainSite.ToString("D")));

        Button(section, $"End consent, to Desktop agent given {When(Morning)}").Click();

        app.WaitForAssertion(() => IsAsked(app).ShouldBeTrue());
        Dialog(app).TextContent.ShouldContain("Desktop agent loses its access with your account at once");
        Ends().ShouldBeEmpty();

        Button(Section(app, Heading), "End consent").Click();

        app.WaitForAssertion(() => Names(Section(app, Heading)).ShouldBe(["Editor agent"]));
        Message(app, Heading).ShouldBe("Your consent to Desktop agent is ended.");
        Ends().ShouldBe([$"mcp/consents/{Desktop:D}"]);
        Management.Calls.Single(call => call.Method == HttpMethod.Delete).Body.ShouldBeNull();
        Management.Count("mcp/consents").ShouldBe(2);
        app.WaitForAssertion(() => ShouldBeFocused<AccountMcpConsents>(app, "messageElement"));
    }

    [Fact]
    public void KeepingTheConsentAtTheQuestionEndsNothing()
    {
        AddConsent(Desktop, "Desktop agent", consentedAt: null, active: true);
        Serve();
        var app = Load(cards: 6);
        Section(app, Heading).TextContent.ShouldContain("Your consent holds");

        Button(Section(app, Heading), "End consent, to Desktop agent").Click();
        app.WaitForAssertion(() => IsAsked(app).ShouldBeTrue());
        Button(Section(app, Heading), "Keep it").Click();

        app.WaitForAssertion(() => IsAsked(app).ShouldBeFalse());
        Ends().ShouldBeEmpty();
        Names(Section(app, Heading)).ShouldBe(["Desktop agent"]);
        Message(app, Heading).ShouldBeEmpty();
    }

    [Fact]
    public void AUserWithoutConsentsIsToldSo()
    {
        Serve();

        var app = Load(cards: 6);

        Section(app, Heading).TextContent.ShouldContain("You allow no MCP client.");
        Section(app, Heading).TextContent.ShouldContain("also one whose consent no longer holds");
    }

    [Fact]
    public void WhileThePluginIsNotEnabledInTheSiteTheSectionIsHiddenAndNothingIsRead()
    {
        Management.Plugins[MainSite].Clear();
        Management.Plugins[MainSite].Add(Plugin(AccountConsentsApi.Plugin, enabled: false));

        var app = Load();

        app.WaitForAssertion(() => Management.Count("plugins").ShouldBe(1));
        app.FindAll("section.cms-card").Count.ShouldBe(5);
        Management.Count("mcp/consents").ShouldBe(0);
    }

    // A host without the plugin has no route, which answers a 404 of no problem, and the shell's list of plugins may be older
    // than the plugin's state in the site, which answers FCMS1550.
    [Theory]
    [InlineData(null)]
    [InlineData("FCMS1550")]
    public void ARouteThatAnswersAsAPluginThatIsOffHidesTheSection(string? code)
    {
        Management.Answer = (method, path) => path == "mcp/consents"
            ? code is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Problem(HttpStatusCode.NotFound, code)
            : null;

        var app = Load();

        app.WaitForAssertion(() => Management.Count("mcp/consents").ShouldBe(1));
        app.WaitForAssertion(() => app.FindAll("section.cms-card").Count.ShouldBe(5));
        app.FindAll("section.cms-card h2").Select(heading => heading.TextContent).ShouldNotContain(Heading);
    }

    [Fact]
    public async Task ASwitchToASiteWithoutThePluginHidesTheSectionAndOneBackReadsItAgain()
    {
        Management.AddSite(Other, "Other", "other");
        AddConsent(Desktop, "Desktop agent", Morning, active: true);
        Serve();
        var app = Load(cards: 6);
        var context = Services.GetRequiredService<AdminContext>();

        await app.InvokeAsync(() => context.SwitchSiteAsync(Other));

        app.WaitForAssertion(() => app.FindAll("section.cms-card").Count.ShouldBe(5));

        await app.InvokeAsync(() => context.SwitchSiteAsync(MainSite));

        app.WaitForAssertion(() => Names(Section(app, Heading)).ShouldBe(["Desktop agent"]));
        Management.Requests.Where(request => request.Path == "mcp/consents").Select(request => request.Site).ShouldBe([MainSite.ToString("D"), MainSite.ToString("D")]);
    }

    [Fact]
    public void AReadTheServerDoesNotAnswerOffersToTryAgain()
    {
        var failing = true;
        AddConsent(Desktop, "Desktop agent", Morning, active: true);
        Serve(read: () => failing ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null);
        var app = Load(cards: 6);
        Section(app, Heading).TextContent.ShouldContain("This could not be read.");

        failing = false;
        Button(Section(app, Heading), "Try again").Click();

        app.WaitForAssertion(() => Names(Section(app, Heading)).ShouldBe(["Desktop agent"]));
    }

    // A client that no longer counts and an end that crossed another change are read again; a failure keeps the list.
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "FCMS1603", "Desktop agent no longer counts toward your clients: it was removed, or your consent was ended already.", 2)]
    [InlineData(HttpStatusCode.Conflict, "FCMS1609", "Desktop agent changed at the same moment, so its place was not freed. End the consent again.", 2)]
    [InlineData(HttpStatusCode.InternalServerError, "FCMS9000", "The consent could not be ended. Try again.", 1)]
    public void EachRefusalOfAnEndIsSaidInItsOwnWords(HttpStatusCode status, string code, string text, int reads)
    {
        AddConsent(Desktop, "Desktop agent", Morning, active: true);
        Serve(end: _ => ServerProblem(status, code, "Refused", "Refused."));
        var app = Load(cards: 6);

        EndDesktop(app);

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe(text));
        Management.Count("mcp/consents").ShouldBe(reads);
        Names(Section(app, Heading)).ShouldBe(["Desktop agent"]);
        IsAsked(app).ShouldBeFalse();
        app.WaitForAssertion(() => ShouldBeFocused<AccountMcpConsents>(app, "messageElement"));
    }

    // The server could not take the lock of the consents in time and changed nothing, so the same end again works.
    [Fact]
    public void AnEndTheLockOfTheConsentsHeldBackCanBeTriedAgain()
    {
        AddConsent(Desktop, "Desktop agent", Morning, active: true);
        var busy = true;
        Serve(end: _ =>
        {
            if (busy)
            {
                busy = false;
                return ServerProblem(HttpStatusCode.ServiceUnavailable, "FCMS3106", "Service unavailable", "Other consents to MCP clients are being changed.");
            }

            consents.Clear();
            return NoContent();
        });
        var app = Load(cards: 6);

        EndDesktop(app);

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Other consents are being changed, so nothing was ended. Try again in a moment."));
        Names(Section(app, Heading)).ShouldBe(["Desktop agent"]);
        Button(Section(app, Heading), $"End consent, to Desktop agent given {When(Morning)}").HasAttribute("disabled").ShouldBeFalse();

        EndDesktop(app);

        app.WaitForAssertion(() => Message(app, Heading).ShouldBe("Your consent to Desktop agent is ended."));
        Section(app, Heading).TextContent.ShouldContain("You allow no MCP client.");
        Ends().ShouldBe([$"mcp/consents/{Desktop:D}", $"mcp/consents/{Desktop:D}"]);
    }

    private void EndDesktop(IRenderedComponent<AdminApp> app)
    {
        Button(Section(app, Heading), $"End consent, to Desktop agent given {When(Morning)}").Click();
        app.WaitForAssertion(() => IsAsked(app).ShouldBeTrue());
        Button(Section(app, Heading), "End consent").Click();
    }

    private static bool IsAsked(IRenderedComponent<AdminApp> app) => app.FindComponent<AccountMcpConsents>().FindComponent<CmsDialog>().Instance.Open;

    private static IElement Dialog(IRenderedComponent<AdminApp> app) => Section(app, Heading).QuerySelector("fluent-dialog")!;

    private List<string> Ends() => [.. Management.Calls.Where(call => call.Method == HttpMethod.Delete).Select(call => call.Path)];

    // GET /mcp/consents answers the consents of the test, or what read answers; DELETE /mcp/consents/{id} what end answers.
    private void Serve(Func<HttpResponseMessage?>? read = null, Func<Guid, HttpResponseMessage>? end = null) => Management.Answer = (method, path) =>
    {
        if (method == HttpMethod.Get && path == "mcp/consents")
        {
            return read?.Invoke() ?? Json(new JsonObject { ["items"] = consents.DeepClone() });
        }

        return method == HttpMethod.Delete && path.StartsWith("mcp/consents/", StringComparison.Ordinal) && end is not null
            ? end(Guid.Parse(path["mcp/consents/".Length..]))
            : null;
    };

    private void AddConsent(Guid id, string name, string? consentedAt, bool active) => consents.Add(new JsonObject
    {
        ["id"] = id,
        ["clientId"] = $"mcp-{id:N}",
        ["name"] = name,
        ["redirectUris"] = new JsonArray("http://127.0.0.1:33418/callback"),
        ["registeredAt"] = "2026-11-10T08:00:00+00:00",
        ["consentedAt"] = consentedAt,
        ["active"] = active,
    });
}
