using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Jobs;

// The jobs page: the list with its filters in the address, the status of a running job that follows without a reload, the
// dead-lettered jobs with retry, and the failed event deliveries with retry, remove and purge, each asked first.
public sealed class JobsPageTests : JobsTestContext
{
    public JobsPageTests()
    {
        UseNeutralResources();
        GrantArea("jobs.manage", instanceOnly: true);
    }

    private static string[][] ListRows(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table[role=grid] tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray())];

    private static AngleSharp.Dom.IElement Filter(IRenderedComponent<AdminApp> app, string label)
    {
        var field = app.FindAll("main [role=group] label").Single(item => item.TextContent.Trim() == label);
        return app.Find($"#{field.GetAttribute("for")}");
    }

    // A row's button by the start of its accessible text, which ends with the row's time.
    private static AngleSharp.Dom.IElement Starting(AngleSharp.Dom.IElement scope, string name) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim().StartsWith(name + ",", StringComparison.Ordinal));

    [Fact]
    public void EachJobShowsItsQueueStatusAndAttemptsAndTheFiltersAreInTheAddress()
    {
        var app = Load("jobs");

        app.WaitForAssertion(() => ListRows(app).Select(row => row[..4]).ShouldBe(
        [
            ["content.publish", "default", "Running", "1 of 5"],
            ["search.rebuild", "default", "Dead-lettered", "2 of 5"],
            ["mail.send", "mail", "Pending", "0 of 5"],
            ["media.cleanup", "default", "Succeeded", "1 of 5"],
        ]));
        JobQueries.ShouldBe(["page=1&pageSize=25"]);
        app.FindAll("main form[role=search]").ShouldBeEmpty();

        Filter(app, "Status").Change("pending");

        app.WaitForAssertion(() => ListRows(app).Select(row => row[0]).ShouldBe(["mail.send"]));
        JobQueries[^1].ShouldBe("page=1&pageSize=25&status=pending");
        Navigation.Uri.ShouldEndWith("/jobs?status=pending");

        Filter(app, "Status").Change("");
        app.WaitForAssertion(() => ListRows(app).Length.ShouldBe(4));
        Filter(app, "Type").Input("search.rebuild");
        Filter(app, "Queue").Input("default");
        app.Find("main form.jobs-names").Submit();

        app.WaitForAssertion(() => ListRows(app).Select(row => row[0]).ShouldBe(["search.rebuild"]));
        JobQueries[^1].ShouldBe("page=1&pageSize=25&type=search.rebuild&queue=default");
        Navigation.Uri.ShouldEndWith("/jobs?type=search.rebuild&queue=default");
    }

    [Fact]
    public void FiltersInTheAddressAreReadOnTheFirstVisitAndShownInTheFields()
    {
        var app = Load("jobs?status=dead_lettered&queue=default");

        app.WaitForAssertion(() => ListRows(app).Select(row => row[0]).ShouldBe(["search.rebuild"]));
        JobQueries.ShouldBe(["page=1&pageSize=25&status=dead_lettered&queue=default"]);
        app.WaitForAssertion(() => Filter(app, "Status").GetAttribute("value").ShouldBe("dead_lettered"));
        Filter(app, "Queue").GetAttribute("value").ShouldBe("default");
        Filter(app, "Type").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void EnterOnARowOpensTheJob()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => ListRows(app).Length.ShouldBe(4));

        var row = app.FindAll("main table[role=grid] tbody tr")[1];
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe($"jobs/{Rebuild:D}");
        row.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        app.WaitForAssertion(() => Heading(app).ShouldBe("search.rebuild"));
        Navigation.Uri.ShouldEndWith($"/jobs/{Rebuild:D}");
    }

    [Fact]
    public void ARunningJobsStatusIsReadAgainEveryFiveSecondsUntilItNoLongerRuns()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => ListRows(app)[0][2].ShouldBe("Running"));

        // Only the running job waits, and nothing is read before its wait ends.
        Eventually(() => Time.Waits.Count(wait => wait == TimeSpan.FromSeconds(5)) == 1);
        JobReads(Publish).ShouldBe(0);

        Time.Tick();
        Eventually(() => JobReads(Publish) == 1 && Time.Waits.Count(wait => wait == TimeSpan.FromSeconds(5)) == 1);
        ListRows(app)[0][2].ShouldBe("Running");

        (JobOf(Publish)["status"], JobOf(Publish)["completedAt"]) = ("succeeded", Now);
        Time.Tick();

        app.WaitForAssertion(() => ListRows(app)[0][2].ShouldBe("Succeeded"));
        JobReads(Publish).ShouldBe(2);
        Eventually(() => Time.Waits.All(wait => wait != TimeSpan.FromSeconds(5)));
        JobQueries.Count.ShouldBe(1);
    }

    [Fact]
    public void ADeadLetteredJobIsRetriedAndLeavesTheDeadLettersAndTheListIsReadAgain()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Dead-lettered jobs")).Length.ShouldBe(1));
        Rows(Section(app, "Dead-lettered jobs"))[0][..3].ShouldBe(["search.rebuild", "default", "2"]);
        Rows(Section(app, "Dead-lettered jobs"))[0][4].ShouldBe(TimedOutShown);
        app.WaitForAssertion(() => ListRows(app).Length.ShouldBe(4));
        var reads = JobQueries.Count;

        Section(app, "Dead-lettered jobs").QuerySelector("tbody fluent-button")!.Click();

        app.WaitForAssertion(() => Message(Section(app, "Dead-lettered jobs")).ShouldBe("Retried search.rebuild: the job is pending again."));
        Section(app, "Dead-lettered jobs").TextContent.ShouldContain("No job is dead-lettered.");
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == $"jobs/{Rebuild:D}/retry");
        app.WaitForAssertion(() => JobQueries.Count.ShouldBe(reads + 1));
        app.WaitForAssertion(() => ListRows(app)[1][2].ShouldBe("Pending"));
    }

    [Fact]
    public void ADeadLetteredJobThatTheRetentionRemovedLeavesTheListAndThePageSaysSo()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Dead-lettered jobs")).Length.ShouldBe(1));
        Jobs.Remove(JobOf(Rebuild));

        Section(app, "Dead-lettered jobs").QuerySelector("tbody fluent-button")!.Click();

        app.WaitForAssertion(() => Message(Section(app, "Dead-lettered jobs")).ShouldBe("search.rebuild no longer exists: jobs are removed 30 days after they end."));
        Rows(Section(app, "Dead-lettered jobs")).ShouldBeEmpty();
    }

    [Fact]
    public void AFailedDeliveryIsRetriedAndAnotherRemovedEachAfterTheQuestion()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(3));
        Rows(Section(app, "Failed event deliveries"))[0][..3].ShouldBe(["EntryPublishedEvent", "search.content.published", "10"]);

        Starting(Section(app, "Failed event deliveries"), "Retry EntryPublishedEvent to search.content.published").Click();

        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain("EntryPublishedEvent is delivered to search.content.published again, with all its attempts."));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("events/dead-letter/", StringComparison.Ordinal));
        Button(app.Find("main fluent-dialog"), "Retry").Click();

        app.WaitForAssertion(() => Message(Section(app, "Failed event deliveries")).ShouldBe("EntryPublishedEvent is delivered to search.content.published again."));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == $"events/dead-letter/{Published:D}/retry");
        Rows(Section(app, "Failed event deliveries")).Select(row => row[0]).ShouldBe(["EntryTrashedEvent", "EntryPurgedEvent"]);

        Starting(Section(app, "Failed event deliveries"), "Remove EntryTrashedEvent to search.content.trashed").Click();
        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain("The failed delivery of EntryTrashedEvent to search.content.trashed is removed, and never delivered."));
        Button(app.Find("main fluent-dialog"), "Remove").Click();

        app.WaitForAssertion(() => Message(Section(app, "Failed event deliveries")).ShouldBe("The failed delivery of EntryTrashedEvent to search.content.trashed is removed."));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == $"events/dead-letter/{Trashed:D}");
        Rows(Section(app, "Failed event deliveries")).Select(row => row[0]).ShouldBe(["EntryPurgedEvent"]);
    }

    [Fact]
    public void AQuestionAnsweredNoChangesNothing()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(3));

        Starting(Section(app, "Failed event deliveries"), "Remove EntryTrashedEvent to search.content.trashed").Click();
        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain("never delivered"));
        Button(app.Find("main fluent-dialog"), "Cancel").Click();

        Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(3);
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Delete);
    }

    [Fact]
    public void TheDeliveriesOfAHandlerArePurgedAfterTheQuestionFromTheHandlersOfTheRowsRead()
    {
        Deliveries.Add(Delivery(Guid.NewGuid(), "EntryPublishedEvent", "search.content.trashed", "The index is locked."));
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(4));
        var purge = Section(app, "Failed event deliveries").QuerySelector("form.jobs-purge select")!;
        purge.QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Choose a handler", "search.content.published", "search.content.purged", "search.content.trashed"]);

        purge.Change("search.content.trashed");
        Section(app, "Failed event deliveries").QuerySelector("form.jobs-purge")!.Submit();

        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain("Every failed delivery to search.content.trashed is removed, also those not shown here, and none of them is delivered."));
        Button(app.Find("main fluent-dialog"), "Purge").Click();

        app.WaitForAssertion(() => Message(Section(app, "Failed event deliveries")).ShouldBe("6 failed deliveries to search.content.trashed are removed."));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Delete && call.Path == "events/dead-letter");
        Rows(Section(app, "Failed event deliveries")).Select(row => row[1]).ShouldBe(["search.content.published", "search.content.purged"]);
        Section(app, "Failed event deliveries").QuerySelectorAll("form.jobs-purge option").Select(option => option.TextContent).ShouldBe(["Choose a handler", "search.content.published", "search.content.purged"]);
    }

    [Fact]
    public void AFailedDeliveryThatIsGoneMeanwhileLeavesTheListAndThePageSaysSo()
    {
        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(3));
        Deliveries.RemoveAt(2);

        Starting(Section(app, "Failed event deliveries"), "Retry EntryPurgedEvent to search.content.purged").Click();
        Button(app.Find("main fluent-dialog"), "Retry").Click();

        app.WaitForAssertion(() => Message(Section(app, "Failed event deliveries")).ShouldBe("The failed delivery of EntryPurgedEvent to search.content.purged no longer exists: failed deliveries are removed 30 days after they occur."));
        Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(2);
    }

    [Fact]
    public void TheDeadLettersHaveNoTotalSoMoreReadsTheNextPageWhileThePageReadWasFull()
    {
        Deliveries.Clear();
        for (var index = 0; index < 26; index++)
        {
            Deliveries.Add(Delivery(Guid.NewGuid(), $"Event{index:D2}", "handler", "failed"));
        }

        var app = Load("jobs");
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(25));

        Button(Section(app, "Failed event deliveries"), "More").Click();

        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(26));
        HasButton(Section(app, "Failed event deliveries"), "More").ShouldBeFalse();
        Management.Calls.Count(call => call.Path == "events/dead-letter" && call.Method == HttpMethod.Get).ShouldBe(2);
    }

    // jobs.manage held through a role of a site opens no call of the area, so neither page is shown and nothing is read.
    [Theory]
    [InlineData("jobs")]
    [InlineData("jobs/0193a1b2-0000-7000-8000-0000000000b1")]
    public void ACallerWhoHoldsJobsManageInASiteOnlyGetsNoAccessAndNoRead(string route)
    {
        GrantArea("jobs.manage", instanceOnly: false);

        var app = Load(route);

        app.WaitForAssertion(() => Heading(app).ShouldBe(Text("Shell.NoAccess.Heading")));
        Management.Calls.ShouldNotContain(call => call.Path.StartsWith("jobs", StringComparison.Ordinal) || call.Path.StartsWith("events", StringComparison.Ordinal));
        Loader.Loads.ShouldBe(0);
        Links(app).ShouldNotContain("jobs");
    }

    [Fact]
    public void AListThatTheServerRefusesShowsWhy()
    {
        Management.Answer = (method, path) => path == "events/dead-letter" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;

        var app = Load("jobs");

        app.WaitForAssertion(() => Section(app, "Failed event deliveries").QuerySelector("[role=alert]")!.TextContent.ShouldStartWith("The failed deliveries could not be read."));
        Management.Answer = null;
        Section(app, "Failed event deliveries").QuerySelector("[role=alert] fluent-button")!.Click();
        app.WaitForAssertion(() => Rows(Section(app, "Failed event deliveries")).Length.ShouldBe(3));
    }
}
