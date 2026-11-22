using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Jobs;

// The page of one job: its state and attempts, retry of a dead-lettered job, which is then followed until it ends, cancel of
// a pending one after the question, and the answers of a job that changed or went meanwhile.
public sealed class JobPageTests : JobsTestContext
{
    public JobPageTests()
    {
        UseNeutralResources();
        GrantArea("jobs.manage", instanceOnly: true);
    }

    private static string[][] Attempts(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("main table.jobs-history tbody tr").Select(row => row.Children.Select(cell => cell.TextContent.Trim()).ToArray())];

    private static string Fact(IRenderedComponent<AdminApp> app, string term) =>
        app.FindAll("main dl.jobs-facts dt").Single(item => item.TextContent == term).NextElementSibling!.TextContent.Trim();

    private static string MessageOf(IRenderedComponent<AdminApp> app) => app.Find("main p.jobs-message").TextContent;

    [Fact]
    public void AFailedJobShowsItsAttemptsAndOneAbandonedPastItsTimeoutSaysSo()
    {
        var app = Load($"jobs/{Rebuild:D}");

        app.WaitForAssertion(() => Heading(app).ShouldBe("search.rebuild"));
        Fact(app, "Status").ShouldBe("Dead-lettered");
        Fact(app, "Queue").ShouldBe("default");
        Fact(app, "Site").ShouldBe("The instance");
        Fact(app, "Attempts").ShouldBe("2 of 5");
        Fact(app, "Last error").ShouldBe(TimedOutShown);
        var attempts = Attempts(app);
        attempts.Select(row => (row[0], row[3])).ShouldBe([("1", "Failed"), ("2", "Its worker stopped")]);
        attempts[0][4].ShouldBe(TimedOutShown);
        attempts[1][4].ShouldBe("The worker stopped without finishing this attempt, and its lease expired.");
        app.FindAll("main fluent-button").Where(button => button.Closest("fluent-dialog") is null).Select(button => button.TextContent.Trim()).ShouldBe(["Retry"]);
        app.FindAll("main a[href='jobs']").Select(link => link.TextContent).ShouldContain("Back to the jobs");
    }

    [Fact]
    public void ARetriedJobIsFollowedEveryFiveSecondsUntilItEnds()
    {
        var app = Load($"jobs/{Rebuild:D}");
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Dead-lettered"));
        Time.Waits.ShouldNotContain(TimeSpan.FromSeconds(5));
        var announced = new List<string>();
        Services.GetRequiredService<FluentCMS.Admin.Accessibility.Announcer>().Announced += (message, _) => announced.Add(message);

        app.Find("main fluent-button").Click();

        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Pending"));
        MessageOf(app).ShouldBe("Retried: the job is pending again, with as many attempts again as a new job gets.");
        Fact(app, "Attempts").ShouldBe("2 of 7");
        app.Find("main").TextContent.ShouldContain("The status is read again every five seconds while it may change.");
        Eventually(() => Time.Waits.Contains(TimeSpan.FromSeconds(5)));
        var reads = JobReads(Rebuild);

        JobOf(Rebuild)["status"] = "running";
        History[Rebuild].Add(Attempt(3, null, null));
        Time.Tick();

        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Running"));
        Attempts(app)[2][3].ShouldBe("Under way");
        announced.ShouldContain("The job is now Running.");

        (JobOf(Rebuild)["status"], JobOf(Rebuild)["completedAt"]) = ("succeeded", Now);
        History[Rebuild][2] = Attempt(3, "succeeded", null);
        Eventually(() => Time.Waits.Contains(TimeSpan.FromSeconds(5)));
        Time.Tick();

        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Succeeded"));
        JobReads(Rebuild).ShouldBe(reads + 2);
        announced.ShouldContain("The job is now Succeeded.");
        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldNotContain("every five seconds"));
        Eventually(() => !Time.Waits.Contains(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void APendingJobIsCancelledAfterTheQuestionAndGoesAmongTheDeadLetters()
    {
        var app = Load($"jobs/{Mail:D}");
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Pending"));

        // Due in an hour, so nothing follows it.
        Time.Waits.ShouldNotContain(TimeSpan.FromSeconds(5));
        app.Find("main fluent-button").Click();
        app.WaitForAssertion(() => app.Find("main fluent-dialog").TextContent.ShouldContain("mail.send does not run, and goes among the dead letters, where it can be retried."));
        Management.Calls.ShouldNotContain(call => call.Method == HttpMethod.Post);
        Button(app.Find("main fluent-dialog"), "Cancel the job").Click();

        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Dead-lettered"));
        MessageOf(app).ShouldBe("Cancelled: the job is among the dead letters, where it can be retried.");
        Fact(app, "Last error").ShouldBe("The job was cancelled before it ran.");
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == $"jobs/{Mail:D}/cancel");
    }

    [Fact]
    public void AJobThatStartedBeforeItsCancelIsShownAsItIsNow()
    {
        var app = Load($"jobs/{Mail:D}");
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Pending"));
        app.Find("main fluent-button").Click();
        JobOf(Mail)["status"] = "running";

        Button(app.Find("main fluent-dialog"), "Cancel the job").Click();

        app.WaitForAssertion(() => MessageOf(app).ShouldBe("Not cancelled: only a pending job is cancelled, and this one has started meanwhile. It is shown as it is now."));
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Running"));
    }

    [Fact]
    public void AJobTheRetentionRemovedShowsAsNotFound()
    {
        var app = Load($"jobs/{Rebuild:D}");
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Dead-lettered"));
        Jobs.Remove(JobOf(Rebuild));

        app.Find("main fluent-button").Click();

        app.WaitForAssertion(() => MessageOf(app).ShouldBe("The job no longer exists: jobs are removed 30 days after they end."));
        app.Find("main").TextContent.ShouldContain("There is no such job. Jobs are removed 30 days after they end.");
        Heading(app).ShouldBe("Job");
    }

    [Fact]
    public void AJobThatCannotBeReadOffersToTryAgain()
    {
        Management.Answer = (method, path) => path == $"jobs/{Publish:D}" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load($"jobs/{Publish:D}");

        app.WaitForAssertion(() => app.Find("main [role=alert]").TextContent.ShouldStartWith("The job could not be read."));
        Management.Answer = null;
        app.Find("main [role=alert] fluent-button").Click();

        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Running"));
        Attempts(app).Single()[3].ShouldBe("Under way");
    }

    [Fact]
    public void ARunningJobThatTheRetentionRemovesWhileFollowedShowsAsNotFound()
    {
        var app = Load($"jobs/{Publish:D}");
        app.WaitForAssertion(() => Fact(app, "Status").ShouldBe("Running"));
        Eventually(() => Time.Waits.Contains(TimeSpan.FromSeconds(5)));
        Jobs.Remove(JobOf(Publish));

        Time.Tick();

        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain("There is no such job."));
        Eventually(() => !Time.Waits.Contains(TimeSpan.FromSeconds(5)));
    }
}
