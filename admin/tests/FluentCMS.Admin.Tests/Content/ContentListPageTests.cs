using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

public sealed class ContentListPageTests : AdminTestContext
{
    private static readonly Guid Ada = new("0193a1b2-0000-7000-8000-0000000000ad");
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");
    private static readonly string StoredKey = $"{ColumnStorageInterop.Prefix}{Ada:N}/{MainSite:N}/article";

    public ContentListPageTests()
    {
        UseNeutralResources();
        Management.UserId = Ada;
        Management.SetTypes(MainSite, Article(), Type("author", fields: [Field("name", required: true)]));
        Management.Entries["article"] = [Entry(1, "Hello", "news"), Entry(2, "World", "video")];
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheListShowsTheLabelAndTheDefaultColumnsAndReadsOnlyThem()
    {
        var app = Load();

        Headers(app).ShouldBe(["Title", "Status", "Updated"]);
        Rows(app).Select(row => Cells(row)[0]).ShouldBe(["Hello", "World"]);
        Rows(app)[0].QuerySelector("a")!.GetAttribute("href").ShouldBe($"content/article/{Id(1)}");
        Rows(app)[0].QuerySelector("a")!.GetAttribute("tabindex").ShouldBe("-1");
        Cells(Rows(app)[0])[1].ShouldBe("Draft");
        Management.ContentQueries.Single().ShouldBe("sort=-updatedAt&page=1&pageSize=25&fields=title,status,updatedAt&withTotal=true");
        app.Find("main h1").TextContent.ShouldBe("Article");
    }

    [Fact]
    public void TheColumnsOfTheQueryStringAreShownInTheirOrderAndARelationIsReadWithItsLabel()
    {
        var app = Load("content/article?columns=author,category,updatedBy,body,nothing");

        Headers(app).ShouldBe(["Title", "Author", "Category", "Updated by"]);
        Cells(Rows(app)[0]).ShouldBe(["Hello", "Grace", "news", "You"]);
        Management.ContentQueries.Single().ShouldBe("sort=-updatedAt&page=1&pageSize=25&fields=title,author.name,category,updatedBy&populate=author&withTotal=true");
    }

    [Fact]
    public void TheChosenColumnsAreKeptInTheBrowserPerUserSiteAndType()
    {
        JSInterop.Setup<string?>("localStorage.getItem", StoredKey).SetResult("category,bad name!,status");

        var app = Load();

        Headers(app).ShouldBe(["Title", "Category", "Status"]);
    }

    [Fact]
    public void ChoosingAndOrderingColumnsKeepsTheChoiceAndPutsItInTheAddress()
    {
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        var app = Load();

        Button(app, "Columns").Click();
        Check(app, "Category", true);
        Check(app, "Status", false);
        // Category moves up to the top of the list, from after the system columns.
        while (app.FindAll(".content-list-columns li")[0].TextContent.Contains("Category", StringComparison.Ordinal) is false)
        {
            app.FindAll(".content-list-columns li").Single(item => item.TextContent.Contains("Category", StringComparison.Ordinal))
                .QuerySelectorAll("fluent-button")[0].Click();
        }

        Button(app, "Apply").Click();

        JSInterop.VerifyInvoke("localStorage.setItem").Arguments.ShouldBe([StoredKey, "category,updatedAt"]);
        Navigation.Uri.ShouldEndWith("content/article?columns=category%2CupdatedAt");
        app.WaitForAssertion(() => Headers(app).ShouldBe(["Title", "Category", "Updated"]));
    }

    [Fact]
    public void SortingByAHeaderPutsTheSortInTheAddressAndShiftAddsAKey()
    {
        var app = Load();

        SortBy(app, "Title");

        Navigation.Uri.ShouldEndWith("content/article?sort=title");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=title&page=1"));

        SortBy(app, "Updated", adding: true);

        Navigation.Uri.ShouldEndWith("content/article?sort=title%2CupdatedAt");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=title,updatedAt&page=1"));
        app.Find("th[aria-sort=ascending]").TextContent.ShouldContain("Title");
    }

    [Fact]
    public void ASortOfTheAddressOnAFieldThatDoesNotSortFallsBackToTheDefault()
    {
        Load("content/article?sort=-author,status");

        Management.ContentQueries.Single().ShouldStartWith("sort=-updatedAt&");
    }

    [Fact]
    public void TheListPagesWithATotal()
    {
        Management.Total = 60;
        var app = Load("content/article?page=2&pageSize=10");

        Management.ContentQueries.Single().ShouldBe("sort=-updatedAt&page=2&pageSize=10&fields=title,status,updatedAt&withTotal=true");
        app.Find(".content-list-pager").TextContent.ShouldContain("60 entries");
        app.Find(".content-list-pager").TextContent.ShouldContain("Page 2 of 6");
        app.Find("table").GetAttribute("aria-rowcount").ShouldBe("61");
        Rows(app)[0].GetAttribute("aria-rowindex").ShouldBe("12");

        Button(app, "Next page").Click();

        Navigation.Uri.ShouldEndWith("content/article?page=3&pageSize=10");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldContain("page=3&pageSize=10"));

        Button(app, "Previous page").Click();
        Button(app, "Previous page").Click();

        Navigation.Uri.ShouldEndWith("content/article?pageSize=10");
    }

    [Fact]
    public void SelectingAllThatMatchIsToldApartFromAllOnThePage()
    {
        Management.Total = 60;
        var app = Load();

        app.Find("thead button[role=checkbox]").Click();

        app.Find(".content-list-selection").TextContent.ShouldContain("2 selected on this page.");

        Button(app, "Select all 60 that match").Click();

        app.Find(".content-list-selection").TextContent.ShouldContain("All 60 entries that match are selected.");
        app.WaitForAssertion(() => app.FindAll("[role=status] > div").Select(message => message.TextContent).ShouldContain("All 60 entries that match are selected."));

        Rows(app)[0].QuerySelector("input[type=checkbox]")!.Change(false);

        app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page.");
    }

    [Fact]
    public void NewIsShownOnlyToACallerWhoMayCreateEntriesOfTheType()
    {
        var allowed = Load();
        Buttons(allowed).ShouldContain("New");
        Button(allowed, "New").Click();
        Navigation.Uri.ShouldEndWith("content/article/new");
    }

    [Fact]
    public void ACallerWhoMayNotCreateSeesNoNewButton()
    {
        Management.Access[MainSite]["types"]!["article"]!["create"] = false;

        var app = Load();

        Buttons(app).ShouldNotContain("New");
        Buttons(app).ShouldContain("Columns");
    }

    [Fact]
    public void ARefusedListIsShownAsTheAnswer()
    {
        Management.Answer = (method, path) => path == "content/article" ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;

        var app = Load();

        app.Find(".content-list-problem").TextContent.ShouldContain("Your roles do not let you read the entries of this type.");
        app.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void ATypeThatIsNotInTheSchemaIsNotFound()
    {
        var app = Load("content/secret");

        app.Find("main h1").TextContent.ShouldBe("Content type not found");
        Management.ContentQueries.ShouldBeEmpty();
    }

    [Fact]
    public void EnterOnARowOpensTheEntry()
    {
        var app = Load();

        Rows(app)[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Navigation.Uri.ShouldEndWith($"content/article/{Id(2)}");
    }

    [Fact]
    public void AnotherTypeIsReadWithItsOwnColumns()
    {
        Management.Entries["author"] = [new JsonObject { ["id"] = Id(9), ["status"] = "published", ["name"] = "Grace" }];
        var app = Load();

        Navigation.NavigateTo("content/author");

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Author"));
        app.WaitForAssertion(() => Rows(app).Select(row => Cells(row)[0]).ShouldBe(["Grace"]));
        Headers(app).ShouldBe(["Name", "Status", "Updated"]);
        Management.ContentQueries.Last().ShouldBe("sort=-updatedAt&page=1&pageSize=25&fields=name,status,updatedAt&withTotal=true");
    }

    [Fact]
    public void ASwitchOfSiteLeavesAnEntryOfASiteTypeForItsListAndKeepsAGlobalOne()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(Shop, Article(), Type("tag", scope: "global"));
        Management.SetTypes(MainSite, Article(), Type("tag", scope: "global"));
        var app = Load($"content/article/{Id(1)}");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("content/article"));

        Navigation.NavigateTo($"content/tag/{Id(3)}");
        app.Find(".site-switcher select").Change(MainSite.ToString("D"));

        app.WaitForAssertion(() => Management.Count("me/access").ShouldBeGreaterThanOrEqualTo(3));
        Navigation.Uri.ShouldEndWith($"content/tag/{Id(3)}");
    }

    [Fact]
    public void FocusMovesToTheHeadingFromOneTypeToAnotherAndNotOnASort()
    {
        Management.Entries["author"] = [];
        var app = Load();
        var before = HeadingFocuses();

        Navigation.NavigateTo("content/author");

        app.WaitForAssertion(() => HeadingFocuses().ShouldBe(before + 1));

        Navigation.NavigateTo("content/author?sort=name");

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=name&"));
        HeadingFocuses().ShouldBe(before + 1);
    }

    [Fact]
    public void AMalformedQueryStringReadsTheDefaults()
    {
        var app = Load("content/article?page=abc&pageSize=99999999999&sort=%2C-&columns=%2C%2C");

        Management.ContentQueries.Single().ShouldBe("sort=-updatedAt&page=1&pageSize=25&fields=title,status,updatedAt&withTotal=true");
        Headers(app).ShouldBe(["Title", "Status", "Updated"]);
    }

    [Fact]
    public void APagePastTheEndGoesToTheLastPage()
    {
        Management.Total = 60;
        Management.Entries["article"] = [];

        var app = Load("content/article?page=9");

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("content/article?page=3"));
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldContain("page=3&"));
    }

    [Fact]
    public void BackThroughTheHistoryReadsTheSortAndPageOfEachStep()
    {
        Management.Total = 60;
        var app = Load();
        SortBy(app, "Title");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=title&page=1"));
        Button(app, "Next page").Click();
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=title&page=2"));

        // Back is a navigation to the address before, which is how the browser's history reaches the page.
        Navigation.NavigateTo("content/article?sort=title");

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=title&page=1"));
        app.Find(".content-list-pager").TextContent.ShouldContain("Page 1 of 3");

        Navigation.NavigateTo("content/article");

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=-updatedAt&page=1"));
        app.Find("th[aria-sort=descending]").TextContent.ShouldContain("Updated");
    }

    [Fact]
    public void AListTheServerDoesNotFindIsShownAsTheAnswer()
    {
        Management.Answer = (method, path) => path == "content/article" ? Problem(HttpStatusCode.NotFound, "FCMS3100") : null;

        var app = Load();

        app.Find(".content-list-problem").TextContent.ShouldContain("This content type does not exist in the site, or your roles do not let you read it.");
        Buttons(app).ShouldNotContain("Try again");
    }

    [Fact]
    public void AFailureOfTheServerOffersToTryAgain()
    {
        var failing = true;
        Management.Answer = (method, path) => path == "content/article" && failing ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load();
        app.FindAll("table").ShouldBeEmpty();

        failing = false;
        Button(app, "Try again").Click();

        app.WaitForAssertion(() => Rows(app).Count.ShouldBe(2));
        app.FindAll(".content-list-problem").ShouldBeEmpty();
    }

    [Fact]
    public void AFailureOnALaterPageLinksToTheFirst()
    {
        Management.Answer = (method, path) => path == "content/article" ? Problem(HttpStatusCode.BadRequest, "FCMS2207") : null;

        var app = Load("content/article?page=40&sort=title");

        Buttons(app).ShouldContain("Try again");
        var first = app.FindAll(".content-list-problem a").Single(link => link.TextContent == "Go to the first page");
        first.GetAttribute("href")!.ShouldEndWith("content/article?sort=title");
    }

    [Fact]
    public void OneEntryIsSaidInTheSingular()
    {
        Management.Entries["article"] = [Entry(1, "Hello", "news")];

        var app = Load();

        app.Find(".content-list-pager").TextContent.ShouldContain("1 entry");
        app.Find(".content-list-pager").TextContent.ShouldNotContain("1 entries");
    }

    // A pager button that disables itself on the first or last page hands focus to the other.
    [Fact]
    public void ThePagerKeepsFocusWhenItsButtonIsDisabled()
    {
        Management.Total = 60;
        var app = Load("content/article?page=2");
        var focuses = Focuses();

        Button(app, "Next page").Click();

        app.WaitForAssertion(() => Focuses().ShouldBe(focuses + 1));
        app.WaitForAssertion(() => app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Next page").HasAttribute("disabled").ShouldBeTrue());

        Button(app, "Previous page").Click();
        Button(app, "Previous page").Click();

        app.WaitForAssertion(() => Focuses().ShouldBe(focuses + 2));
    }

    [Fact]
    public void MovingAColumnKeepsFocusOnItsButtons()
    {
        var app = Load();
        Button(app, "Columns").Click();
        var focuses = Focuses();

        MoveButton(app, "Updated", "up").Click();

        Focuses().ShouldBe(focuses + 1);
        app.FindAll(".content-list-columns li")[0].TextContent.ShouldContain("Updated");
        MoveButton(app, "Updated", "up").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void ASwitchOfSiteLeavesTheQueryStringOfAList()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(Shop, Article());
        var app = Load("content/article?sort=title&page=2&columns=category");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("content/article"));
    }

    private static JsonObject Article()
    {
        var author = Field("author", "relation");
        author["relation"] = new JsonObject { ["target"] = "author" };
        return Type("article", fields: [Field("title", required: true), Field("category", "select"), author, Field("body", "richText")]);
    }

    private static string Id(int n) => $"0193a1b2-0000-7000-8000-{n:D12}";

    private static JsonObject Entry(int n, string title, string category) => new()
    {
        ["id"] = Id(n),
        ["status"] = "draft",
        ["locale"] = "en",
        ["version"] = 1,
        ["updatedAt"] = "2026-11-01T10:00:00Z",
        ["updatedBy"] = Ada,
        ["title"] = title,
        ["category"] = category,
        ["author"] = new JsonObject { ["id"] = Id(9), ["type"] = "author", ["name"] = "Grace" },
    };

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");

    private static IElement MoveButton(IRenderedComponent<AdminApp> app, string column, string direction) =>
        app.FindAll(".content-list-columns fluent-button").Single(button => button.GetAttribute("aria-label") == $"Move {column} {direction}");

    private int HeadingFocuses() => Document.Invocations.Count(call => call.Identifier == "focusHeading");

    private static List<string> Headers(IRenderedComponent<AdminApp> app) =>
        app.FindAll(".cms-data-grid thead th:not(.cms-data-grid-select)").Select(th => th.QuerySelector("button > span")?.TextContent ?? th.TextContent.Trim()).ToList();

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<AdminApp> app) => app.FindAll(".cms-data-grid tbody tr");

    private static List<string> Cells(IElement row) =>
        row.QuerySelectorAll("td:not(.cms-data-grid-select)").Select(cell => cell.TextContent.Trim()).ToList();

    private static List<string> Buttons(IRenderedComponent<AdminApp> app) =>
        app.FindAll("main fluent-button").Select(button => button.TextContent.Trim()).ToList();

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    private static void Check(IRenderedComponent<AdminApp> app, string label, bool value)
    {
        var box = app.FindComponents<CmsCheckbox>().Single(found => found.Instance.Label == label);
        box.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private static void SortBy(IRenderedComponent<AdminApp> app, string title, bool adding = false) =>
        app.FindAll(".cms-data-grid th button").Single(button => button.QuerySelector("span")!.TextContent == title).Click(new MouseEventArgs { ShiftKey = adding });

    private IRenderedComponent<AdminApp> Load(string route = "content/article")
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        if (route.StartsWith("content/article", StringComparison.Ordinal) && !route.Contains("/0193", StringComparison.Ordinal))
        {
            app.WaitForAssertion(() => app.FindAll("table[aria-busy]").ShouldBeEmpty());
        }

        return app;
    }
}
