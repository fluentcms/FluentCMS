using System.Net;
using Bunit;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Diagnostics;

// The diagnostics page: the startup report with its insecure defaults, pending migrations, drift, plugins and providers, and
// the detailed health, which is read again on request.
public sealed class DiagnosticsPageTests : DiagnosticsTestContext
{
    public DiagnosticsPageTests()
    {
        UseNeutralResources();
        GrantArea("system.read", instanceOnly: true);
    }

    [Fact]
    public void TheStartupReportShowsTheInsecureDefaultsMigrationsDriftAndProviders()
    {
        var app = Load("system/diagnostics");

        app.WaitForAssertion(() => Section(app, "Startup report").QuerySelector("dl").ShouldNotBeNull());
        var report = Section(app, "Startup report");
        report.QuerySelectorAll("dl")[0].QuerySelectorAll("dd").Select(item => item.TextContent.Trim()).Where((_, index) => index != 2)
            .ShouldBe(["1.4.0", "Production", "Sqlite 1.4.0", "Yes", "Off"]);
        report.QuerySelector("ul.diagnostics-insecure li")!.TextContent.ShouldBe("webhooks-private-networks: Webhooks may deliver to private networks.");
        report.TextContent.ShouldContain("The schema in code differs from the one last applied to the database.");
        report.QuerySelector("pre")!.TextContent.ShouldBe("+ review.rating");
        report.QuerySelectorAll("table")[0].QuerySelector("tbody tr")!.TextContent.ShouldContain("20261120_AddReviews");
        report.QuerySelectorAll("table")[1].QuerySelector("tbody tr")!.Children.Select(cell => cell.TextContent.Trim()).ShouldBe(["ISearchIndex", "SearchIndex", "Plugin", "search"]);
        report.TextContent.ShouldContain("search 1.0.0");
        report.QuerySelector("details summary")!.TextContent.ShouldBe("Assemblies of FluentCMS (1)");
        report.QuerySelector("[role=alert]").ShouldBeNull();
    }

    [Fact]
    public void TheHealthShowsEveryCheckAndIsRunAgainOnRequest()
    {
        var app = Load("system/diagnostics");
        app.WaitForAssertion(() => Section(app, "Health").QuerySelectorAll("tbody tr").Length.ShouldBe(2));
        var health = Section(app, "Health");
        health.QuerySelector("p")!.TextContent.ShouldStartWith("Degraded, in ");
        var outbox = health.QuerySelectorAll("tbody tr")[1].Children.Select(cell => cell.TextContent.Trim()).ToArray();
        (outbox[0], outbox[1], outbox[3]).ShouldBe(("outbox", "Degraded", "ready"));
        outbox[4].ShouldBe("42 deliveries wait.Error: TimeoutException: The drain is late.");
        var reads = Management.Count("system/health");

        Health["status"] = "Healthy";
        Health["checks"]!.AsArray().RemoveAt(1);
        Section(app, "Health").QuerySelector("fluent-button")!.Click();

        app.WaitForAssertion(() => Section(app, "Health").QuerySelectorAll("tbody tr").Length.ShouldBe(1));
        Section(app, "Health").QuerySelector("p")!.TextContent.ShouldStartWith("Healthy, in ");
        Section(app, "Health").QuerySelector(".diagnostics-message")!.TextContent.ShouldBe("The checks were run again.");
        Management.Count("system/health").ShouldBe(reads + 1);
        Management.Count("system/diagnostics").ShouldBe(1);
    }

    [Fact]
    public void AReportNotMadeYetSaysSoAndIsReadAgainOnRequest()
    {
        var made = false;
        Management.Answer = (method, path) => path == "system/diagnostics" && !made ? Problem(HttpStatusCode.ServiceUnavailable, "FCMS2001") : null;
        var app = Load("system/diagnostics");

        app.WaitForAssertion(() => Section(app, "Startup report").QuerySelector("[role=alert]")!.TextContent.ShouldStartWith("The startup report is made once the host has started. Try again in a moment."));
        made = true;
        Section(app, "Startup report").QuerySelector("[role=alert] fluent-button")!.Click();

        app.WaitForAssertion(() => Section(app, "Startup report").QuerySelector("dl").ShouldNotBeNull());
    }

    [Fact]
    public void AnIncompleteReportSaysWhatKeptItFromBeingMade()
    {
        Report["failure"] = "DbException";
        Report["pendingMigrations"] = new System.Text.Json.Nodes.JsonArray();
        Report["insecureDefaults"] = new System.Text.Json.Nodes.JsonArray();

        var app = Load("system/diagnostics");

        app.WaitForAssertion(() => Section(app, "Startup report").QuerySelector("[role=alert]")!.TextContent.ShouldStartWith("The report is not complete: the host met DbException while it made it"));
        Section(app, "Startup report").TextContent.ShouldContain("None: the database is up to date.");
        Section(app, "Startup report").TextContent.ShouldContain("None: no setting is one that only suits a developer machine.");
    }

    [Fact]
    public void HealthThatCannotBeReadAgainKeepsTheChecksShown()
    {
        var app = Load("system/diagnostics");
        app.WaitForAssertion(() => Section(app, "Health").QuerySelectorAll("tbody tr").Length.ShouldBe(2));

        Management.Answer = (method, path) => path == "system/health" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        Section(app, "Health").QuerySelector("fluent-button")!.Click();

        app.WaitForAssertion(() => Section(app, "Health").QuerySelector(".diagnostics-message")!.TextContent.ShouldBe("The checks could not be run again; those shown are from before."));
        Section(app, "Health").QuerySelectorAll("tbody tr").Length.ShouldBe(2);
    }

    // system.read held through a role of a site opens no call of the area, so the page is not shown and nothing is read.
    [Fact]
    public void ACallerWhoHoldsSystemReadInASiteOnlyGetsNoAccessAndNoRead()
    {
        GrantArea("system.read", instanceOnly: false);

        var app = Load("system/diagnostics");

        app.WaitForAssertion(() => Heading(app).ShouldBe(Text("Shell.NoAccess.Heading")));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("system", StringComparison.Ordinal));
        Loader.Loads.ShouldBe(0);
        Links(app).ShouldNotContain("system/diagnostics");
    }
}
