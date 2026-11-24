using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Jobs;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Jobs;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class JobsLocalizationTests : JobsTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheJobsPageComesFromTheResources(string culture)
    {
        MarkData(culture);

        var app = LoadLocalized(culture, "jobs", shown =>
        {
            shown.FindAll("main table[role=grid] tbody tr").Count.ShouldBe(4);
            Rows(Section(shown, Localized<JobsResources>("Jobs.DeadJobs.Heading"))).Length.ShouldBe(1);
            Rows(Section(shown, Localized<JobsResources>("Jobs.Events.Heading"))).Length.ShouldBe(3);
        });

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<JobsResources>("Jobs.Page.Heading"));

        // The question of a purge and its answer.
        var events = Section(app, Localized<JobsResources>("Jobs.Events.Heading"));
        var handler = (string)Deliveries[0]!["handler"]!;
        events.QuerySelector("form.jobs-purge select")!.Change(handler);
        events.QuerySelector("form.jobs-purge")!.Submit();
        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain(Localized<JobsResources>("Jobs.Confirm.PurgeText", handler)));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
        Button(app.Find("main fluent-dialog"), Localized<JobsResources>("Jobs.Confirm.Purge")).Click();
        app.WaitForAssertion(() => Message(Section(app, Localized<JobsResources>("Jobs.Events.Heading"))).ShouldNotBeEmpty());
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfAJobsPageComesFromTheResources(string culture)
    {
        MarkData(culture);

        var app = LoadLocalized(culture, $"jobs/{Rebuild:D}", shown => shown.FindAll("main table.jobs-history tbody tr").Count.ShouldBe(2));

        // A retry, which the page then follows.
        app.FindAll("main fluent-button").First(button => button.Closest("fluent-dialog") is null).Click();
        app.WaitForAssertion(() => app.Find("main p.jobs-message").TextContent.ShouldBe(Localized<JobsResources>("Jobs.Job.Retried")));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check: the types,
    // queues, origins and errors of the jobs and their attempts, and the events, handlers and errors of the failed deliveries,
    // which the page shows as they are. An error that starts with the runner's code keeps it, which the page reads.
    private void MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);
        void MarkMembers(JsonNode node, params string[] members)
        {
            foreach (var member in members)
            {
                if (node[member] is { } value)
                {
                    var text = (string)value!;
                    node[member] = text.StartsWith("FCMS2508: ", StringComparison.Ordinal) ? "FCMS2508: " + Mark(text["FCMS2508: ".Length..]) : Mark(text);
                }
            }
        }

        foreach (var job in Jobs)
        {
            MarkMembers(job!, "type", "queue", "origin", "lastError");
        }

        foreach (var attempt in History.Values.SelectMany(attempts => attempts))
        {
            MarkMembers(attempt!, "error");
        }

        foreach (var delivery in Deliveries)
        {
            MarkMembers(delivery!, "eventType", "handler", "lastError");
        }
    }
}
