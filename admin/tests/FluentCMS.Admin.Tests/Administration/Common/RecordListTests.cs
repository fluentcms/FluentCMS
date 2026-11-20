using System.Net;
using Bunit;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Common;

// The frame of a list page of records: the search and the page in the address, and the states of the content lists.
public sealed class RecordListTests : AdminTestContext
{
    private readonly List<RecordQuery> reads = [];

    private Func<RecordQuery, RecordPage<Row>> answer = _ => new([], 0);

    public RecordListTests()
    {
        UseNeutralResources();
        Start();
        Navigation.NavigateTo("users");
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheFirstReadAsksForTheFirstPageWithoutASearchAndShowsItsRows()
    {
        answer = _ => new([new("Ada"), new("Grace")], 2);

        var list = RenderList();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Ada", "Grace"]));
        reads.ShouldBe([new RecordQuery(null, 1, 25)]);
        list.Find("table").GetAttribute("aria-label").ShouldBe("Users");
        list.Find("nav[aria-label='Pages']").TextContent.ShouldContain("2 in all");
        list.Find("nav[aria-label='Pages']").TextContent.ShouldContain("Page 1 of 1");
    }

    [Fact]
    public void TheAddressGivesTheSearchAndThePageSoAReloadKeepsThem()
    {
        Navigation.NavigateTo("users?q=ada&page=3");
        answer = _ => new([new("Ada")], 55);

        var list = RenderList();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Ada"]));
        reads.ShouldBe([new RecordQuery("ada", 3, 25)]);
        list.Find("input").GetAttribute("value").ShouldBe("ada");
        list.Find("nav[aria-label='Pages']").TextContent.ShouldContain("Page 3 of 3");
    }

    [Fact]
    public void ASearchGoesToItsFirstPageInTheAddressReadsAgainAndAnnouncesWhatItFinds()
    {
        Navigation.NavigateTo("users?page=2");
        answer = query => query.Search is null ? new([new("Ada")], 30) : new([new("Grace")], 1);
        var announced = new List<string>();
        Services.GetRequiredService<FluentCMS.Admin.Accessibility.Announcer>().Announced += (message, _) => announced.Add(message);
        var list = RenderList();
        list.WaitForAssertion(() => Names(list).ShouldBe(["Ada"]));
        announced.ShouldBeEmpty();

        list.Find("input").Input("  grace ");
        list.Find("form").Submit();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Grace"]));
        Navigation.Uri.ShouldEndWith("/users?q=grace");
        reads.ShouldBe([new RecordQuery(null, 2, 25), new RecordQuery("grace", 1, 25)]);
        announced.ShouldBe(["1 found."]);
    }

    [Fact]
    public void TheNextPageIsReadFromTheAddressAndThePreviousButtonIsOffOnTheFirst()
    {
        answer = query => new([new($"Page {query.Page}")], 60);
        var list = RenderList();
        list.WaitForAssertion(() => Names(list).ShouldBe(["Page 1"]));
        Button(list, "Previous page").HasAttribute("disabled").ShouldBeTrue();

        Button(list, "Next page").Click();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Page 2"]));
        Navigation.Uri.ShouldEndWith("/users?page=2");
        Button(list, "Previous page").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void AListWithoutRecordsSaysSoAndASearchThatFindsNoneSaysThat()
    {
        var list = RenderList();
        list.WaitForAssertion(() => list.Find(".cms-data-grid-message").TextContent.Trim().ShouldBe("No users yet."));

        Navigation.NavigateTo("users?q=nobody");

        list.WaitForAssertion(() => list.Find(".cms-data-grid-message").TextContent.Trim().ShouldBe("Nothing matches the search."));
    }

    [Fact]
    public void WhileItReadsTheGridIsBusy()
    {
        var held = new TaskCompletionSource<RecordPage<Row>>();

        var list = RenderList(read: (_, _) => held.Task);

        list.Find("table").GetAttribute("aria-busy").ShouldBe("true");
        held.SetResult(new([new("Ada")], 1));
        list.WaitForAssertion(() => Names(list).ShouldBe(["Ada"]));
        list.Find("table").HasAttribute("aria-busy").ShouldBeFalse();
    }

    [Fact]
    public void ARefusalIsShownAsTheAnswerWithoutARetry()
    {
        var list = RenderList(read: (_, _) => throw new ApiProblemException(new ApiProblem { Status = 403, Code = "FCMS3103", Title = "Forbidden", TraceId = "00-abc" }));

        list.WaitForAssertion(() => list.Find("[role=alert]").TextContent.ShouldContain("Your roles do not let you read this list."));
        list.Find("[role=alert]").TextContent.ShouldContain("Trace id: 00-abc");
        list.FindAll("[role=alert] fluent-button").ShouldBeEmpty();
        list.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void AFailureThatIsNoAnswerOffersTheRetryWhichReadsAgain()
    {
        var failing = true;
        var list = RenderList(read: (_, _) => failing ? throw new HttpRequestException("offline") : Task.FromResult(new RecordPage<Row>([new("Ada")], 1)));
        list.WaitForAssertion(() => list.Find("[role=alert]").TextContent.ShouldContain("The list could not be read."));
        failing = false;

        Button(list, "Try again").Click();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Ada"]));
    }

    [Fact]
    public void APagePastTheLastGoesToTheLastPage()
    {
        Navigation.NavigateTo("users?page=9");
        answer = query => query.Page > 2 ? new([], 30) : new([new($"Page {query.Page}")], 30);

        var list = RenderList();

        list.WaitForAssertion(() => Names(list).ShouldBe(["Page 2"]));
        Navigation.Uri.ShouldEndWith("/users?page=2");
    }

    [Fact]
    public async Task AReloadReadsTheSamePageAgain()
    {
        answer = _ => new([new($"Read {reads.Count}")], 1);
        var list = RenderList();
        list.WaitForAssertion(() => Names(list).ShouldBe(["Read 1"]));

        await list.InvokeAsync(() => list.Instance.ReloadAsync());

        list.WaitForAssertion(() => Names(list).ShouldBe(["Read 2"]));
    }

    [Fact]
    public void AListThatIsNotSearchableHasNoSearchAndReadsNone()
    {
        Navigation.NavigateTo("jobs?q=ada");

        var list = RenderList(searchable: false);

        list.WaitForAssertion(() => reads.ShouldBe([new RecordQuery(null, 1, 25)]));
        list.FindAll("form").ShouldBeEmpty();
    }

    private IRenderedComponent<RecordList<Row>> RenderList(Func<RecordQuery, CancellationToken, Task<RecordPage<Row>>>? read = null, bool searchable = true) =>
        Render<RecordList<Row>>(parameters => parameters
            .Add(list => list.Label, "Users")
            .Add(list => list.EmptyText, "No users yet.")
            .Add(list => list.Searchable, searchable)
            .Add(list => list.Read, read ?? ((query, _) =>
            {
                reads.Add(query);
                return Task.FromResult(answer(query));
            }))
            .Add(list => list.Columns, builder =>
            {
                builder.OpenComponent<CmsDataGridColumn<Row>>(0);
                builder.AddAttribute(1, nameof(CmsDataGridColumn<Row>.Title), "Name");
                builder.AddAttribute(2, nameof(CmsDataGridColumn<Row>.Value), (Func<Row, string?>)(row => row.Name));
                builder.CloseComponent();
            }));

    private static List<string> Names(IRenderedComponent<RecordList<Row>> list) =>
        [.. list.FindAll("tbody tr:not(.cms-data-grid-message)").Select(row => row.QuerySelectorAll("td")[0].TextContent.Trim())];

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<RecordList<Row>> list, string text) =>
        list.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    public sealed record Row(string Name);
}
