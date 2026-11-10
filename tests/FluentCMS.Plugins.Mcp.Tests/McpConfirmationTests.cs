using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.DataProtection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Tests;

// The confirmation token alone: what it is bound to, how long it lasts and which instance reads it.
public sealed class McpConfirmationTests
{
    private static readonly Guid Entry = Guid.NewGuid();
    private static readonly Principal Editor = new(PrincipalKind.AdminUser, Guid.NewGuid(), "Editor");
    private static readonly SiteId Site = new(Guid.NewGuid());

    private readonly ControllableTimeProvider _time = new();
    private readonly EphemeralDataProtectionProvider _ring = new();

    [Fact]
    public void ATokenConfirmsThePlanItWasMadeForUntilFiveMinutesHavePassed()
    {
        var confirmations = new McpConfirmations(_ring, _time);
        var plan = Plan();

        var (token, expiresAt) = confirmations.Issue(plan);

        expiresAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(_time.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds()));
        confirmations.Check(token, plan).ShouldBe(McpConfirmation.Confirmed);
        confirmations.Check(token, Plan(arguments: """{ "confirmationToken": "x", "dryRun": false, "id": "1" }""")).ShouldBe(McpConfirmation.Confirmed);
        confirmations.Check(null, plan).ShouldBe(McpConfirmation.Missing);
        confirmations.Check("", plan).ShouldBe(McpConfirmation.Missing);

        _time.Advance(TimeSpan.FromSeconds(299));
        confirmations.Check(token, plan).ShouldBe(McpConfirmation.Confirmed);
        _time.Advance(TimeSpan.FromSeconds(1));
        confirmations.Check(token, plan).ShouldBe(McpConfirmation.Expired);
    }

    [Fact]
    public void ATokenOfAnotherToolArgumentPrincipalClientSiteOrLocaleIsNotOfTheCall()
    {
        var confirmations = new McpConfirmations(_ring, _time);
        var (token, _) = confirmations.Issue(Plan());

        confirmations.Check(token, Plan(tool: "writer_delete")).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(arguments: """{ "id": "2" }""")).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(arguments: """{ "id": "1", "limit": 5 }""")).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(principal: new Principal(PrincipalKind.AdminUser, Guid.NewGuid(), "Editor"))).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(principal: new Principal(PrincipalKind.ApiKey, Editor.Id, "Editor"))).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(client: "another-client")).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(client: null)).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(site: new SiteId(Guid.NewGuid()))).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token, Plan(locale: "fr")).ShouldBe(McpConfirmation.Invalid);
    }

    [Fact]
    public void ATokenOfAPlanThatHasChangedSaysSo()
    {
        var confirmations = new McpConfirmations(_ring, _time);
        var other = Guid.NewGuid();
        var (token, _) = confirmations.Issue(Plan(entries: [new McpPlanEntry(Entry, 3, "draft"), new McpPlanEntry(other, 1, "published")]));

        confirmations.Check(token, Plan(entries: [new McpPlanEntry(other, 1, "published"), new McpPlanEntry(Entry, 3, "draft")])).ShouldBe(McpConfirmation.Confirmed);
        confirmations.Check(token, Plan(entries: [new McpPlanEntry(Entry, 4, "draft"), new McpPlanEntry(other, 1, "published")])).ShouldBe(McpConfirmation.PlanChanged);
        confirmations.Check(token, Plan(entries: [new McpPlanEntry(Entry, 3, "draft"), new McpPlanEntry(other, 1, "unpublished")])).ShouldBe(McpConfirmation.PlanChanged);
        confirmations.Check(token, Plan(entries: [new McpPlanEntry(Entry, 3, "draft")])).ShouldBe(McpConfirmation.PlanChanged);
        confirmations.Check(token, Plan(entries: [new McpPlanEntry(Entry, 3, "draft"), new McpPlanEntry(other, 1, "published"), new McpPlanEntry(Guid.NewGuid(), 1, "draft")])).ShouldBe(McpConfirmation.PlanChanged);
    }

    // The keys of an object sign in one order, so the same arguments confirm however a client wrote them.
    [Fact]
    public void TheSameArgumentsConfirmInAnyOrderOfTheirKeys()
    {
        var confirmations = new McpConfirmations(_ring, _time);
        var (token, _) = confirmations.Issue(Plan(arguments: """{ "type": "post", "filter": { "note": { "eq": "a" }, "rating": { "gte": 2 } }, "limit": 5, "locale": null }"""));

        confirmations.Check(token, Plan(arguments: """{ "limit": 5, "filter": { "rating": { "gte": 2 }, "note": { "eq": "a" } }, "type": "post" }""")).ShouldBe(McpConfirmation.Confirmed);
        confirmations.Check(token, Plan(arguments: """{ "limit": 5, "filter": { "rating": { "gte": 3 }, "note": { "eq": "a" } }, "type": "post" }""")).ShouldBe(McpConfirmation.Invalid);
    }

    [Fact]
    public void ATokenThatWasChangedOrThatIsNoTokenIsRefused()
    {
        var confirmations = new McpConfirmations(_ring, _time);
        var plan = Plan();
        var (token, _) = confirmations.Issue(plan);
        var parts = token.Split('.');

        parts.Length.ShouldBe(5);
        string With(int index, string value) => string.Join('.', parts.Select((part, position) => position == index ? value : part));

        // A later end, other entries, a key of the caller's own making and another MAC.
        confirmations.Check(With(1, (long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) + 600).ToString(System.Globalization.CultureInfo.InvariantCulture)), plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(With(2, Convert.ToBase64String(new byte[32]).TrimEnd('=')), plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(With(3, Convert.ToBase64String(new byte[32]).TrimEnd('=')), plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(With(4, Convert.ToBase64String(new byte[32]).TrimEnd('=')), plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(With(0, "v2"), plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check("not a token", plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(token + ".more", plan).ShouldBe(McpConfirmation.Invalid);
        confirmations.Check(new string('a', 5000), plan).ShouldBe(McpConfirmation.Invalid);
    }

    [Fact]
    public void AnInstanceReadsTheTokenOfAnotherOnlyWithTheKeyRingTheyShare()
    {
        var first = new McpConfirmations(_ring, _time);
        var second = new McpConfirmations(_ring, _time);
        var stranger = new McpConfirmations(new EphemeralDataProtectionProvider(), _time);
        var plan = Plan();

        var (token, _) = first.Issue(plan);
        var (again, _) = first.Issue(plan);
        var (ofSecond, _) = second.Issue(plan);

        second.Check(token, plan).ShouldBe(McpConfirmation.Confirmed);
        first.Check(ofSecond, plan).ShouldBe(McpConfirmation.Confirmed);
        stranger.Check(token, plan).ShouldBe(McpConfirmation.Invalid);

        // An instance has one key, and two instances have two.
        again.Split('.')[3].ShouldBe(token.Split('.')[3]);
        ofSecond.Split('.')[3].ShouldNotBe(token.Split('.')[3]);
    }

    private McpPlan Plan(
        string tool = "post_delete",
        string arguments = """{ "id": "1" }""",
        Principal? principal = null,
        string? client = "client-a",
        SiteId? site = null,
        string locale = "en",
        IReadOnlyList<McpPlanEntry>? entries = null)
    {
        using var document = JsonDocument.Parse(arguments);
        var operation = new OperationContext(principal ?? Editor, Origin.Mcp, site ?? Site, locale, client, "correlation", _time.GetUtcNow());
        return new McpPlan(operation, tool, document.RootElement.Clone(), entries ?? [new McpPlanEntry(Entry, 1, "draft")]);
    }
}
