using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// Locale-aware editing (A-17): the switcher on lists and forms with the locale in the address, the access and schema read in
// it, the status of every locale, a locale started empty or as a copy and added with If-Match "0", the status controls in the
// form's locale, the side by side, and a field's direction from its content locale.
public sealed class ContentLocaleTests : AdminTestContext
{
    private readonly FakeContent _content;

    public ContentLocaleTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.AddLocale(MainSite, "fr", 1);
        Management.SetTypes(MainSite, Post(), Type("tag", fields: [Field("name")]));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private AdminContext Context => Services.GetRequiredService<AdminContext>();

    [Fact]
    public void ALocalizedListHasALinkPerLocaleWithTheDefaultWithoutOne()
    {
        Management.Entries["post"] = [];

        var app = Load("content/post");

        Locales(app).ShouldBe([("en", "content/post?locale=en", true), ("fr", "content/post?locale=fr", false)]);
    }

    [Fact]
    public void ATypeThatIsNotLocalizedHasNoSwitcher()
    {
        Management.Entries["tag"] = [];

        var app = Load("content/tag");

        app.FindAll(".content-locales").ShouldBeEmpty();
    }

    [Fact]
    public void AListInAnotherLocaleReadsTheAccessTheSchemaAndThePageInItAndTheOtherLocalesStatusByIds()
    {
        var id = Guid.CreateVersion7();
        Management.Entries["post"] = [Row(id, "Bonjour", "fr", "draft")];
        Management.LocaleEntries[("post", "en")] = [Row(id, null, "en", "published")];

        var app = Load("content/post?locale=fr");

        Management.MetadataLocales.ShouldBe([("me/access", null), ("schema", null), ("me/access", "fr"), ("schema", "fr")]);
        Management.ContentQueries[0].ShouldNotContain("locale=");
        Management.ContentQueries[1].ShouldBe($"filter[id][in]={id:D}&pageSize=1&fields=id,status,scheduledPublishAt,scheduledUnpublishAt&locale=en");
        app.WaitForAssertion(() => Badges(app.Find(".cms-data-grid tbody tr")).ShouldBe(["en Published", "fr Draft"]));
        app.Find(".cms-data-grid tbody tr a").GetAttribute("href").ShouldBe($"content/post/{id:D}?locale=fr");
        Locales(app).ShouldBe([("en", "content/post?locale=en", false), ("fr", "content/post?locale=fr", true)]);
    }

    [Fact]
    public void ALocaleTheSiteDoesNotHaveIsTheDefault()
    {
        Management.Entries["post"] = [];

        Load("content/post?locale=de");

        Context.ContentLocale.ShouldBe("en");
        Management.MetadataLocales.ShouldAllBe(read => read.Locale == null);
    }

    [Fact]
    public void TheFormShowsTheStatusOfEveryLocale()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");

        var app = Load($"content/post/{id:D}");

        Badges(app.Find(".content-form-toolbar")).ShouldBe(["en Published", "fr Not translated"]);
        Locales(app).ShouldBe([("en", $"content/post/{id:D}?locale=en", true), ("fr", $"content/post/{id:D}?locale=fr", false)]);
        _content.Reads.ShouldContain($"/api/management/v1/content/post/{id:D}?fallback=none");
        _content.Reads.ShouldContain($"/api/management/v1/content/post/{id:D}");
    }

    [Fact]
    public void ACreatedEntryShowsItsLocaleAndTheOthersAsNotTranslated()
    {
        var app = Load("content/post/new?locale=fr");
        Input(app, "title").Input("Bonjour");

        Button(app, "Save").Click();

        app.WaitForAssertion(() => Badges(app.Find(".content-form-toolbar")).ShouldBe(["en Not translated", "fr Draft"]));
        _content.Writes.ShouldHaveSingleItem().Locale.ShouldBe("fr");
        Navigation.Uri.ShouldEndWith("?locale=fr");
    }

    [Fact]
    public void ALocaleWithoutATranslationStartsEmptyWithTheSharedFieldsAndIsAddedWithIfMatchZero()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello", ["code"] = "P-1" });

        var app = Load($"content/post/{id:D}?locale=fr");

        app.Find(".content-form-new-locale").TextContent.ShouldContain("no translation in fr");
        Input(app, "title").GetAttribute("value").ShouldBeNullOrEmpty();
        Input(app, "code").GetAttribute("value").ShouldBe("P-1");
        Input(app, "code").HasAttribute("readonly").ShouldBeTrue();

        Input(app, "title").Input("Bonjour");
        Button(app, "Save").Click();

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var write = _content.Writes.Single();
        (write.Method, write.IfMatch, write.Locale, write.Body["title"]!.GetValue<string>()).ShouldBe((HttpMethod.Put, "\"0\"", "fr", "Bonjour"));
        app.WaitForAssertion(() => Badges(app.Find(".content-form-toolbar")).ShouldBe(["en Draft", "fr Draft"]));
        _content.Fields(id)["title"]!.GetValue<string>().ShouldBe("Hello");
        app.FindAll(".content-form-new-locale").ShouldBeEmpty();
    }

    [Fact]
    public void ACopyFromAnotherLocaleFillsTheNewLocaleFromAReadInItAndSavesItAsNew()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello", ["code"] = "P-1" });
        var app = Load($"content/post/{id:D}?locale=fr");

        Button(app, "Copy from en").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Hello"));
        _content.Writes.ShouldBeEmpty();
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeTrue();

        Button(app, "Save").Click();

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        (_content.Writes[0].IfMatch, _content.Writes[0].Locale).ShouldBe(("\"0\"", "fr"));
        _content.Fields(id, "fr")["title"]!.GetValue<string>().ShouldBe("Hello");
    }

    [Fact]
    public void SwitchingTheLocaleOfAFormReadsTheEntryAndTheAccessInTheNewLocale()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        _content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");
        var app = Load($"content/post/{id:D}");

        Navigation.NavigateTo(Locales(app)[1].Href);

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Bonjour"));
        Context.ContentLocale.ShouldBe("fr");
        Management.MetadataLocales[^2..].ShouldBe([("me/access", "fr"), ("schema", "fr")]);
        Locales(app).Select(locale => locale.Current).ShouldBe([false, true]);
    }

    [Fact]
    public void SwitchingTheLocaleOfAFormWithUnsavedChangesAsksFirst()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        _content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");
        var app = Load($"content/post/{id:D}");
        Input(app, "title").Input("Mine");

        Navigation.NavigateTo(Locales(app)[1].Href);

        app.WaitForAssertion(() => Dialog(app, "Leave with unsaved changes?").Instance.Open.ShouldBeTrue());
        Button(app, "Keep editing").Click();
        Navigation.Uri.ShouldBe($"http://localhost/content/post/{id:D}");
        Input(app, "title").GetAttribute("value").ShouldBe("Mine");

        Navigation.NavigateTo(Locales(app)[1].Href);
        app.WaitForAssertion(() => Dialog(app, "Leave with unsaved changes?").Instance.Open.ShouldBeTrue());
        Button(app, "Leave").Click();

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe($"http://localhost/content/post/{id:D}?locale=fr"));
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void TheStatusControlsActOnTheLocaleOfTheForm()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        _content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");
        var app = Load($"content/post/{id:D}?locale=fr");

        Button(app, "Publish").Click();

        app.WaitForAssertion(() => _content.Status(id, "fr").ShouldBe("published"));
        _content.Writes.ShouldHaveSingleItem().Locale.ShouldBe("fr");
        _content.Status(id).ShouldBe("published");
        app.WaitForAssertion(() => Badges(app.Find(".content-form-toolbar")).ShouldBe(["en Published", "fr Published"]));
    }

    [Fact]
    public void SideBySideShowsAnotherLocaleReadOnlyBesideEachFieldInItsDirection()
    {
        Management.AddLocale(MainSite, "ar", 2);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello", ["code"] = "P-1" });
        _content.Add("post", new JsonObject { ["title"] = "مرحبا", ["code"] = "P-1" }, id: id, locale: "ar");
        var app = Load($"content/post/{id:D}");

        var choice = app.FindComponents<CmsSelect>().Single(select => select.Instance.Label == "Side by side with");
        choice.InvokeAsync(() => choice.Instance.ValueChanged.InvokeAsync("ar"));

        app.WaitForAssertion(() => app.FindAll(".content-form-compare").Count.ShouldBe(2));
        var other = app.Find("[data-field='title'] .content-form-compare input");
        (other.GetAttribute("value"), other.GetAttribute("lang"), other.GetAttribute("dir"), other.HasAttribute("readonly")).ShouldBe(("مرحبا", "ar", "rtl", true));
        app.Find("[data-field='title'] .content-form-compare").GetAttribute("aria-label").ShouldBe("Title in ar");
        (Input(app, "title").GetAttribute("value"), Input(app, "title").GetAttribute("dir")).ShouldBe(("Hello", "ltr"));

        choice.InvokeAsync(() => choice.Instance.ValueChanged.InvokeAsync("fr"));
        app.WaitForAssertion(() => app.Find("[data-field='title'] .content-form-compare").TextContent.ShouldContain("No translation in fr"));
    }

    // The direction of a field is its content locale's, also when that is the site's default and the address names none.
    [Theory]
    [InlineData("ar", "rtl")]
    [InlineData("ur", "rtl")]
    [InlineData("en", "ltr")]
    public void AFieldOfAnEntryInTheSiteDefaultLocaleHasItsDirection(string locale, string direction)
    {
        Management.Sites[0]!["defaultLocale"] = locale;
        Management.Sites[0]!["locales"] = new JsonArray(new JsonObject { ["code"] = locale, ["position"] = 0, ["enabled"] = true });
        var id = _content.Add("post", new JsonObject { ["title"] = "Text" });

        var app = Load($"content/post/{id:D}");

        (Input(app, "title").GetAttribute("lang"), Input(app, "title").GetAttribute("dir")).ShouldBe((locale, direction));
    }

    // A translator's grant to change posts is limited to fr: in en the form is read-only, in fr it saves.
    [Fact]
    public void ALocaleTheCallerMayNotWriteIsReadOnly()
    {
        var writes = Management.Access[MainSite].DeepClone().AsObject();
        Management.LocaleAccess[(MainSite, "fr")] = writes;
        foreach (var name in new[] { "create", "update", "publish", "unpublish", "delete" })
        {
            Management.Access[MainSite]["types"]!["post"]![name] = false;
        }

        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        _content.Add("post", new JsonObject { ["title"] = "Bonjour" }, id: id, locale: "fr");

        var app = Load($"content/post/{id:D}");
        app.Find(".content-form").TextContent.ShouldContain("You can read this entry but not change it.");
        Input(app, "title").HasAttribute("readonly").ShouldBeTrue();

        Navigation.NavigateTo(Locales(app)[1].Href);

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Bonjour"));
        Input(app, "title").HasAttribute("readonly").ShouldBeFalse();
        app.FindAll(".content-form-toolbar fluent-button").Select(button => button.TextContent.Trim()).ShouldContain("Save");
    }

    // A translator whose grants are all limited to fr may read no type in en: the sidebar says so and offers the other
    // locales, and the types come with the access and the schema of fr.
    [Fact]
    public void ALocaleWithNoTypeOffersTheOthersAndTheTypesComeWithTheAccessOfTheLocale()
    {
        Management.LocaleAccess[(MainSite, "fr")] = Management.Access[MainSite].DeepClone().AsObject();
        Management.LocaleSchemas[(MainSite, "fr")] = Management.Schemas[MainSite].DeepClone().AsObject();
        Management.Access[MainSite]["types"]!.AsObject().Clear();
        Management.Schemas[MainSite]["types"] = new JsonArray();
        Management.Entries["post"] = [];
        var app = Load("");

        var notice = app.FindAll(".shell-sidebar .shell-sidebar-locales").ShouldHaveSingleItem();
        notice.TextContent.ShouldContain("You can read no content in en.");
        Navigation.NavigateTo(notice.QuerySelectorAll("a")[1].GetAttribute("href")!);

        app.WaitForAssertion(() => app.FindAll(".shell-sidebar a").Select(link => link.TextContent.Trim()).ShouldContain("Post"));
        app.FindAll(".shell-sidebar-locales").ShouldBeEmpty();
        Context.ContentLocale.ShouldBe("fr");

        // A link without a locale, as the sidebar's, keeps it.
        Navigation.NavigateTo("content/post");
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Post"));
        Context.ContentLocale.ShouldBe("fr");
        Management.MetadataLocales.Count(read => read.Locale == "fr").ShouldBe(2);
    }

    [Fact]
    public void TheDefaultLocaleNamedInTheAddressResetsIt()
    {
        Management.Entries["post"] = [];
        var app = Load("content/post?locale=fr");

        Navigation.NavigateTo(Locales(app)[0].Href);

        app.WaitForAssertion(() => (Context.ContentLocale, Context.NamedLocale).ShouldBe(("en", null)));
        Management.MetadataLocales[^2..].ShouldBe([("me/access", null), ("schema", null)]);
        app.WaitForAssertion(() => Locales(app).Select(locale => locale.Current).ShouldBe([true, false]));
    }

    [Fact]
    public void ASiteSwitchResetsTheLocaleToTheNewSitesDefaultAndLeavesItsAddress()
    {
        var second = Guid.NewGuid();
        Management.AddSite(second, "Second", "second");
        Management.AddLocale(second, "fr", 1);
        Management.SetTypes(second, Post());
        Management.Entries["post"] = [];
        var app = Load("content/post?locale=fr");

        var sites = app.FindComponents<CmsSelect>().Single(select => select.Instance.Label == "Site");
        sites.InvokeAsync(() => sites.Instance.ValueChanged.InvokeAsync(second.ToString("D")));

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/content/post"));
        app.WaitForAssertion(() => (Context.Site!.Id, Context.ContentLocale, Context.NamedLocale).ShouldBe((second, "en", null)));
        Management.MetadataLocales[^2..].ShouldBe([("me/access", null), ("schema", null)]);
    }

    private IRenderedComponent<AdminApp> Load(string route)
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Context.Status.ShouldBe(ShellStatus.Ready));
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status'], table[aria-busy]").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static List<(string Code, string Href, bool Current)> Locales(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-locales a").Select(link => (link.TextContent.Trim(), link.GetAttribute("href")!.Replace("http://localhost/", "", StringComparison.Ordinal), link.GetAttribute("aria-current") == "true"))];

    private static List<string> Badges(IElement scope) =>
        [.. scope.QuerySelectorAll(".content-badge").Select(badge => string.Join(' ', badge.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    // The form's own input of the field, not the one of the other locale beside it.
    private static IElement Input(IRenderedComponent<AdminApp> app, string field) =>
        app.FindAll($"[data-field='{field}'] input").First(input => input.Closest(".content-form-compare") is null);

    private static JsonObject Row(Guid id, string? title, string locale, string status)
    {
        var row = new JsonObject { ["id"] = id, ["status"] = status, ["locale"] = locale, ["version"] = 1 };
        if (title is not null)
        {
            row["title"] = title;
        }

        return row;
    }

    // A localized post whose code the locales share.
    private static JsonObject Post()
    {
        var title = Field("title");
        title["localizable"] = true;
        var type = Type("post", fields: [title, Field("code")]);
        type["localized"] = true;
        return type;
    }
}
