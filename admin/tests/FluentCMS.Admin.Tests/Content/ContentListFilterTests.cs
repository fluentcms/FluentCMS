using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The filters, the search and the badges of a list (A-48): kept in the address, read as the API's grammar, and the status of
// each locale and each pending schedule.
public sealed class ContentListFilterTests : AdminTestContext
{
    private const string Fields = "fields=title,status,updatedAt,scheduledPublishAt,scheduledUnpublishAt";

    public ContentListFilterTests()
    {
        UseNeutralResources();
        Management.SetTypes(MainSite, Article());
        Management.Entries["article"] = [Entry(1, "Hello"), Entry(2, "World")];
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheFiltersAndTheSearchOfTheAddressAreReadAndShown()
    {
        var app = Load("content/article?filter[title][contains]=Hel&filter[category][in]=news,video&filter[secret][eq]=x&q=%20tour%20");

        Management.ContentQueries.Single().ShouldBe($"filter[title][contains]=Hel&filter[category][in]=news,video&q=tour&sort=-updatedAt&page=1&pageSize=25&{Fields}&withTotal=true");
        Chips(app).ShouldBe(["Title contains Hel", "Category is one of news, video"]);
        SearchBox(app).Instance.Value.ShouldBe("tour");
    }

    [Fact]
    public void AFilterOnATextFieldIsAddedInTheDialogAndShowsTheFirstPage()
    {
        Management.Total = 60;
        var app = Load("content/article?page=2&sort=title");

        Button(app, "Add filter").Click();
        Choose(app, "Field", "title");
        Choose(app, "Condition", "startsWith");
        Enter(app, "Value", "Wor");
        Button(app, "Apply filter").Click();

        Navigation.Uri.ShouldEndWith("content/article?sort=title&filter[title][startsWith]=Wor");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[title][startsWith]=Wor&sort=title&page=1&"));
        Chips(app).ShouldBe(["Title starts with Wor"]);
    }

    [Fact]
    public void ASelectFilterTakesOptionsAndOffersTheOperatorsOfItsType()
    {
        var app = Load();

        Button(app, "Add filter").Click();
        Choose(app, "Field", "category");
        Options(app, "Condition").ShouldBe(["is", "is not", "is one of", "is none of", "is empty", "is not empty"]);
        Choose(app, "Condition", "in");
        Check(app, "video", true);
        Check(app, "opinion", true);
        Button(app, "Apply filter").Click();

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[category][in]=video,opinion&"));
    }

    // A moment is entered in the user's time zone and sent in UTC; the summary shows it in that zone again.
    [Fact]
    public void ADateTimeFilterIsEnteredInTheUsersTimeZone()
    {
        var app = Load(zone: TimeZoneInfo.CreateCustomTimeZone("Plus two", TimeSpan.FromHours(2), "Plus two", "Plus two"));

        Button(app, "Add filter").Click();
        Choose(app, "Field", "publishedOn");
        Options(app, "Condition").ShouldBe(["is before", "is on or before", "is after", "is on or after", "is between", "is empty", "is not empty"]);
        Choose(app, "Condition", "gt");
        Set<CmsDatePicker, DateOnly?>(app, "Value", new DateOnly(2026, 3, 1));
        Set<CmsTimePicker, TimeOnly?>(app, "Time of Value", new TimeOnly(9, 30));
        Button(app, "Apply filter").Click();

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[publishedOn][gt]=2026-03-01T07%3A30%3A00Z&"));
        Chips(app).Single().ShouldStartWith("Published on is after 3/1/2026");
        Chips(app).Single().ShouldContain("9:30");
    }

    [Fact]
    public void AValueThatDoesNotReadAsTheFieldsTypeIsRefusedInTheDialog()
    {
        var app = Load();
        var reads = Management.ContentQueries.Count;

        Button(app, "Add filter").Click();
        Choose(app, "Field", "author");
        Enter(app, "Value", "not an id");
        Button(app, "Apply filter").Click();

        app.Find(".cms-field-error, [aria-invalid=true]").ShouldNotBeNull();
        app.Markup.ShouldContain("Enter a value of the field's type.");
        Management.ContentQueries.Count.ShouldBe(reads);
    }

    [Fact]
    public void ARelationTakesEntryIdsUntilItsPickerExists()
    {
        var app = Load();

        Button(app, "Add filter").Click();
        Choose(app, "Field", "author");
        Choose(app, "Condition", "in");
        Enter(app, "Values", "0193a1b2-0000-7000-8000-000000000009, 0193A1B2-0000-7000-8000-00000000000A");
        Button(app, "Apply filter").Click();

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[author][in]=0193a1b2-0000-7000-8000-000000000009,0193a1b2-0000-7000-8000-00000000000a&"));
    }

    [Fact]
    public void AFilterIsChangedAndRemovedAndFocusStaysInTheToolbar()
    {
        var app = Load("content/article?filter[title][contains]=Hel&filter[category][eq]=news");
        var focuses = Focuses();

        Button(app, "Title contains Hel").Click();
        Enter(app, "Value", "Wor");
        Button(app, "Apply filter").Click();

        Navigation.Uri.ShouldEndWith("content/article?filter[title][contains]=Wor&filter[category][eq]=news");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[title][contains]=Wor&filter[category][eq]=news&sort="));

        app.WaitForAssertion(() => app.FindAll("fluent-button").Any(button => button.GetAttribute("aria-label") == "Remove the filter: Category is news").ShouldBeTrue());
        app.FindAll("fluent-button").First(button => button.GetAttribute("aria-label") == "Remove the filter: Category is news").Click();

        Navigation.Uri.ShouldEndWith("content/article?filter[title][contains]=Wor");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[title][contains]=Wor&sort="));
        app.WaitForAssertion(() => Chips(app).ShouldBe(["Title contains Wor"]));
        app.WaitForAssertion(() => Focuses().ShouldBe(focuses + 1));
    }

    [Fact]
    public void BackThroughTheHistoryReadsTheFiltersAndSearchOfEachStep()
    {
        var app = Load();

        Set<CmsTextInput, string>(app, "Search", "tour");
        app.Find("form[role=search]").Submit();

        Navigation.Uri.ShouldEndWith("content/article?q=tour");
        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("q=tour&"));

        Navigation.NavigateTo("content/article?filter[title][eq]=Hello");

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[title][eq]=Hello&sort="));
        app.WaitForAssertion(() => SearchBox(app).Instance.Value.ShouldBeNull());
        Chips(app).ShouldBe(["Title is Hello"]);

        // Back is a navigation to the address before, which is how the browser's history reaches the page.
        Navigation.NavigateTo("content/article");

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("sort=-updatedAt&"));
        Chips(app).ShouldBeEmpty();
    }

    // The pager and the sort rewrite the address with Blazor's own escaping, which the filters and the search still read.
    [Fact]
    public void PagingAndSortingKeepTheFiltersAndTheSearch()
    {
        Management.Total = 60;
        var app = Load("content/article?filter[category][in]=news,video&filter[title][contains]=a%20b&q=release%20no");

        Button(app, "Next page").Click();

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[category][in]=news,video&filter[title][contains]=a%20b&q=release%20no&sort=-updatedAt&page=2&"));

        app.FindAll(".cms-data-grid th button").Single(button => button.QuerySelector("span")!.TextContent == "Title").Click();

        app.WaitForAssertion(() => Management.ContentQueries.Last().ShouldStartWith("filter[category][in]=news,video&filter[title][contains]=a%20b&q=release%20no&sort=title&page=1&"));
        Chips(app).ShouldBe(["Category is one of news, video", "Title contains a b"]);
    }

    [Fact]
    public void NoEntryThatMatchesIsSaidApartFromAnEmptyList()
    {
        Management.Entries["article"] = [];

        var app = Load("content/article?q=nothing");

        app.Find(".cms-data-grid").TextContent.ShouldContain("No entries match the search and filters.");
    }

    [Fact]
    public void TheSearchBoxIsAbsentForATypeThatIsNotSearchable()
    {
        Management.SetTypes(MainSite, Type("article", fields: [Field("title", required: true)]));

        var app = Load("content/article?q=tour");

        app.FindAll("form[role=search]").ShouldBeEmpty();
        Management.ContentQueries.Single().ShouldNotContain("q=");
    }

    [Fact]
    public void TheSearchBoxIsAbsentWithoutAnIndex()
    {
        Management.Access[MainSite]["search"] = false;
        var app = Load();
        app.FindAll("form[role=search]").ShouldBeEmpty();
    }

    [Fact]
    public void TheSearchBoxIsAbsentWhereTheSearchPluginIsDisabled()
    {
        Management.Plugins[MainSite].Add(Plugin("search", enabled: false));
        var app = Load();
        app.FindAll("form[role=search]").ShouldBeEmpty();
    }

    [Fact]
    public void TheStatusIsABadge()
    {
        var app = Load();

        Rows(app)[0].QuerySelector(".content-badge-draft")!.TextContent.Trim().ShouldBe("Draft");
        // A browser refuses an attribute name that is not one, which the test's parser would keep.
        app.FindAll(".content-badges, .content-badges *").SelectMany(element => element.Attributes)
            .ShouldAllBe(attribute => attribute.Name == "class" || attribute.Name == "datetime" || attribute.Name.StartsWith("b-", StringComparison.Ordinal));
    }

    [Fact]
    public void WithEveryLocaleAnArticlePublishedInOneLocaleAndMissingInAnotherShowsBothBadges()
    {
        Management.Sites[0]!["locales"] = new JsonArray(
            new JsonObject { ["code"] = "en", ["position"] = 0, ["enabled"] = true },
            new JsonObject { ["code"] = "fr", ["position"] = 1, ["enabled"] = true },
            new JsonObject { ["code"] = "de", ["position"] = 2, ["enabled"] = false });
        Management.Entries["article"] =
        [
            new JsonObject
            {
                ["id"] = Id(1),
                ["locales"] = new JsonObject { ["en"] = Localized(1, "Typed content", "published") },
            },
            new JsonObject
            {
                ["id"] = Id(2),
                ["locales"] = new JsonObject { ["fr"] = Localized(2, "Bienvenue", "modified"), ["en"] = Localized(2, "Welcome", "draft") },
            },
        ];

        var app = Load();

        Management.ContentQueries.Single().ShouldEndWith("&locale=all&withTotal=true");
        Rows(app).Select(row => Cells(row)[0]).ShouldBe(["Typed content", "Welcome"]);
        Badges(Rows(app)[0]).ShouldBe(["en Published", "fr Not translated"]);
        Rows(app)[0].QuerySelectorAll(".content-badge-missing").Single().TextContent.ShouldContain("fr");
        Badges(Rows(app)[1]).ShouldBe(["en Draft", "fr Modified"]);
        app.FindAll(".content-badges, .content-badges *").SelectMany(element => element.Attributes)
            .ShouldAllBe(attribute => attribute.Name == "class" || attribute.Name.StartsWith("b-", StringComparison.Ordinal));
        Rows(app)[1].QuerySelector("a")!.GetAttribute("href").ShouldBe($"content/article/{Id(2)}");
    }

    [Fact]
    public void WithoutTheStatusColumnOrWithOneLocaleTheListReadsOneLocale()
    {
        Management.Sites[0]!["locales"]!.AsArray().Add(new JsonObject { ["code"] = "fr", ["position"] = 1, ["enabled"] = true });

        Load("content/article?columns=updatedAt");

        Management.ContentQueries.Single().ShouldNotContain("locale=");
    }

    // A pending schedule shows its time in the user's time zone: 07:30 UTC is 09:30 at UTC+2.
    [Fact]
    public void APendingScheduleShowsItsTimeInTheUsersTimeZone()
    {
        var entry = Entry(1, "Notes");
        entry["scheduledPublishAt"] = "2027-03-01T07:30:00Z";
        entry["scheduledUnpublishAt"] = "2027-04-01T07:30:00Z";
        Management.Entries["article"] = [entry];

        var app = Load(zone: TimeZoneInfo.CreateCustomTimeZone("Plus two", TimeSpan.FromHours(2), "Plus two", "Plus two"));

        var badges = Rows(app)[0].QuerySelectorAll(".content-badge-schedule");
        badges.Select(badge => Normalize(badge.TextContent)).ShouldBe(["Publishes 3/1/2027 9:30 AM", "Unpublishes 4/1/2027 9:30 AM"]);
        badges[0].QuerySelector("time")!.GetAttribute("datetime").ShouldBe("2027-03-01T07:30:00.0000000+00:00");
    }

    private static JsonObject Article()
    {
        var title = Field("title", required: true);
        title["searchable"] = true;
        var author = Field("author", "relation");
        author["relation"] = new JsonObject { ["target"] = "author" };
        var article = Type(
            "article",
            fields:
            [
                title,
                Field("category", "select", settings: new JsonObject { ["options"] = new JsonArray("news", "video", "opinion") }),
                Field("publishedOn", "dateTime"),
                author,
                Field("body", "richText"),
            ]);
        article["localized"] = true;
        return article;
    }

    private static string Id(int n) => $"0193a1b2-0000-7000-8000-{n:D12}";

    private static JsonObject Entry(int n, string title) => new()
    {
        ["id"] = Id(n),
        ["status"] = "draft",
        ["locale"] = "en",
        ["version"] = 1,
        ["updatedAt"] = "2026-11-01T10:00:00Z",
        ["title"] = title,
    };

    private static JsonObject Localized(int n, string title, string status)
    {
        var entry = Entry(n, title);
        entry.Remove("id");
        entry["status"] = status;
        return entry;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace(' ', ' ');

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");

    private static List<string> Chips(IRenderedComponent<AdminApp> app) =>
        app.FindAll(".content-filter").Select(chip => chip.QuerySelector("fluent-button")!.TextContent.Trim()).ToList();

    private static List<string> Badges(IElement row) =>
        row.QuerySelectorAll(".content-badge").Select(badge => Normalize(badge.TextContent)).ToList();

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<AdminApp> app) => app.FindAll(".cms-data-grid tbody tr");

    private static List<string> Cells(IElement row) =>
        row.QuerySelectorAll("td:not(.cms-data-grid-select)").Select(cell => cell.TextContent.Trim()).ToList();

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    private static IRenderedComponent<CmsTextInput> SearchBox(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsTextInput>().Single(input => input.Instance.Label == "Search");

    private static List<string> Options(IRenderedComponent<AdminApp> app, string label) =>
        [.. app.FindComponents<CmsSelect>().Single(input => input.Instance.Label == label).Instance.Options.Select(option => option.Text)];

    private static void Choose(IRenderedComponent<AdminApp> app, string label, string value) => Set<CmsSelect, string>(app, label, value);

    private static void Enter(IRenderedComponent<AdminApp> app, string label, string value) => Set<CmsTextInput, string>(app, label, value);

    private static void Check(IRenderedComponent<AdminApp> app, string label, bool value) => Set<CmsCheckbox, bool>(app, label, value);

    private static void Set<TInput, TValue>(IRenderedComponent<AdminApp> app, string label, TValue value)
        where TInput : CmsInputBase<TValue>
    {
        var input = app.FindComponents<TInput>().Single(found => found.Instance.Label == label);
        input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private IRenderedComponent<AdminApp> Load(string route = "content/article", TimeZoneInfo? zone = null)
    {
        Start();
        if (zone is not null)
        {
            Services.Replace(ServiceDescriptor.Singleton(new FormatService(System.Globalization.CultureInfo.GetCultureInfo("en-US"), zone)));
        }

        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        app.WaitForAssertion(() => app.FindAll("table[aria-busy]").ShouldBeEmpty());
        return app;
    }
}
