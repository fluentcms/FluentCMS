using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The component and block editors of A-26. A page has blocks of a union (hero, quote, section, and a tower that nests four
// deep), a component and a repeatable component. A section holds cards, and a card a link component, so a link is at depth 3.
public sealed partial class NestedFieldTests : AdminTestContext
{
    private readonly FakeContent _content;

    public NestedFieldTests()
    {
        UseNeutralResources();
        Document.SetupModule(invocation => invocation.Identifier == "rememberFocus");
        Document.SetupVoid("returnFocus", _ => true).SetVoidResult();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Page(), Type("author", fields: [Field("name")]));
        Management.Schemas[MainSite]["components"] = new JsonArray(
            Type("seo", "component", fields: [Field("metaTitle", settings: null)]),
            Type("link", "component", fields: [Field("label"), Field("url", "url")]));
        Management.Schemas[MainSite]["blocks"] = new JsonArray(
            Type("hero", "block", fields: [Field("heading"), Relation("author"), Field("score", "rating")]),
            Type("quote", "block", fields: [Field("text", "longText")]),
            Type("section", "block", fields: [Field("title"), Blocks("cards", "card")]),
            Type("card", "block", fields: [Field("title"), Component("link", "link")]),
            Type("tower", "block", fields: [Blocks("floors", "floor")]),
            Type("floor", "block", fields: [Blocks("rooms", "room")]),
            Type("room", "block", fields: [Component("door", "link")]));
    }

    [Fact]
    public void ABlockIsAddedByTypeWithAFreshIdAndTheBlocksReadKeepTheirIds()
    {
        var id = Page(Hero("heroAAAAAAAA", "Welcome"));
        var app = Load($"content/page/{id:D}");

        ChooseType(app, "quote");
        Click(Actions(app), "Add Quote");
        app.WaitForAssertion(() => Titles(app).ShouldBe(["Hero 1", "Quote 2"]));
        Announced(app).ShouldContain("Quote 2 added.");
        app.WaitForAssertion(() => Focused().ShouldBe(Toggle(app, 1)));
        Items(app)[1].QuerySelector("textarea")!.Input("Less is more");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var sections = _content.Writes[0].Body["sections"]!.AsArray();
        sections[0]!["id"]!.GetValue<string>().ShouldBe("heroAAAAAAAA");
        sections[0]!["heading"]!.GetValue<string>().ShouldBe("Welcome");
        sections[1]!["type"]!.GetValue<string>().ShouldBe("quote");
        sections[1]!["id"]!.GetValue<string>().ShouldMatch(IdPattern().ToString());
        sections[1]!["text"]!.GetValue<string>().ShouldBe("Less is more");
    }

    [Fact]
    public void BlocksAreOrderedByButtonsKeysAndDragAndFocusFollowsTheMovedBlock()
    {
        var id = Page(Hero("aaaaaaaaaaaa", "A"), Hero("bbbbbbbbbbbb", "B"), Hero("cccccccccccc", "C"));
        var app = Load($"content/page/{id:D}");

        IconButton(app, "Move Hero 1 down").Click();
        app.WaitForAssertion(() => Headings(app).ShouldBe(["B", "A", "C"]));
        Announced(app).ShouldContain("Hero 2 moved to position 2 of 3.");
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "downButtons", 1)));

        Heads(app)[1].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        app.WaitForAssertion(() => Headings(app).ShouldBe(["B", "C", "A"]));
        app.WaitForAssertion(() => Focused().ShouldBe(Toggle(app, 2)));

        IconButton(app, "Move Hero 3 up").Click();
        app.WaitForAssertion(() => Headings(app).ShouldBe(["B", "A", "C"]));
        IconButton(app, "Move Hero 2 up").Click();
        app.WaitForAssertion(() => Headings(app).ShouldBe(["A", "B", "C"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "downButtons", 0)));

        Heads(app)[2].DragStart();
        Items(app)[0].DragOver();
        Items(app)[0].Drop();
        app.WaitForAssertion(() => Headings(app).ShouldBe(["C", "A", "B"]));

        Save(app);
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        Ids(_content.Writes[0].Body["sections"]).ShouldBe(["cccccccccccc", "aaaaaaaaaaaa", "bbbbbbbbbbbb"]);
    }

    // A move changes what the items show rather than moving them in the document, and each block's fields go with it.
    [Fact]
    public void AMoveKeepsTheItemsInPlaceAndTheCollapsedStateWithItsBlock()
    {
        var id = Page(Hero("aaaaaaaaaaaa", "A"), Quote("bbbbbbbbbbbb", "B"));
        var app = Load($"content/page/{id:D}");
        var toggle = Toggle(app, 0);
        Items(app)[1].QuerySelector(".nested-toggle")!.Click();
        app.WaitForAssertion(() => Items(app)[1].QuerySelector(".nested-preview")!.TextContent.ShouldBe("B"));

        Heads(app)[1].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        app.WaitForAssertion(() => Titles(app).ShouldBe(["Quote 1", "Hero 2"]));
        Toggle(app, 0).ShouldBe(toggle);
        Items(app)[0].QuerySelector(".nested-toggle")!.GetAttribute("aria-expanded").ShouldBe("false");
        Items(app)[1].QuerySelector(".nested-toggle")!.GetAttribute("aria-expanded").ShouldBe("true");
        Items(app)[1].QuerySelector("input")!.GetAttribute("value").ShouldBe("A");
    }

    [Fact]
    public void ADuplicateAndEveryBlockItHoldsTakeFreshIds()
    {
        var section = new JsonObject
        {
            ["type"] = "section",
            ["id"] = "section00001",
            ["title"] = "Team",
            ["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["id"] = "card00000001", ["title"] = "Ada", ["link"] = new JsonObject { ["label"] = "Profile" } }),
        };
        var id = Page(section);
        var app = Load($"content/page/{id:D}");

        IconButton(app, "Duplicate Section 1").Click();
        app.WaitForAssertion(() => Titles(app).ShouldBe(["Section 1", "Section 2"]));
        Announced(app).ShouldContain("Section 1 duplicated.");
        app.WaitForAssertion(() => Focused().ShouldBe(Toggle(app, 1)));
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var sections = _content.Writes[0].Body["sections"]!.AsArray();
        sections[0]!.ToJsonString().ShouldBe(section.ToJsonString());
        var copy = sections[1]!.AsObject();
        copy["id"]!.GetValue<string>().ShouldMatch(IdPattern().ToString());
        copy["id"]!.GetValue<string>().ShouldNotBe("section00001");
        var card = copy["cards"]![0]!;
        card["id"]!.GetValue<string>().ShouldMatch(IdPattern().ToString());
        card["id"]!.GetValue<string>().ShouldNotBe("card00000001");
        card["link"]!["label"]!.GetValue<string>().ShouldBe("Profile");
        copy["title"]!.GetValue<string>().ShouldBe("Team");
    }

    [Fact]
    public void RemoveGivesFocusToTheNextBlockAndCollapsedBlocksShowTheirFirstText()
    {
        var id = Page(Hero("aaaaaaaaaaaa", "A"), Quote("bbbbbbbbbbbb", "Line one\nline two"), Hero("cccccccccccc", "C"));
        var app = Load($"content/page/{id:D}");

        Click(Actions(app), "Collapse all");
        app.WaitForAssertion(() => Items(app).ShouldAllBe(item => item.QuerySelector(".nested-body") == null));
        Announced(app).ShouldContain("All items collapsed.");
        Items(app)[1].QuerySelector(".nested-preview")!.TextContent.ShouldBe("Line one\nline two");
        Items(app)[1].QuerySelector(".nested-toggle")!.GetAttribute("aria-expanded").ShouldBe("false");

        IconButton(app, "Remove Hero 1").Click();
        app.WaitForAssertion(() => Titles(app).ShouldBe(["Quote 1", "Hero 2"]));
        Announced(app).ShouldContain("Hero 1 removed.");
        app.WaitForAssertion(() => Focused().ShouldBe(Toggle(app, 0)));

        Click(Actions(app), "Expand all");
        app.WaitForAssertion(() => Items(app).ShouldAllBe(item => item.QuerySelector(".nested-body") != null));
        Items(app)[0].QuerySelector(".nested-toggle")!.Click();
        app.WaitForAssertion(() => Items(app)[0].QuerySelector(".nested-body").ShouldBeNull());

        Save(app);
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        Ids(_content.Writes[0].Body["sections"]).ShouldBe(["bbbbbbbbbbbb", "cccccccccccc"]);
    }

    // Decision 20: a tower nests four deep and is never offered; a section nests three deep and is offered at the top only.
    [Fact]
    public void TheAddPickerOffersNoTypeThatWouldNestDeeperThanTheSchemaAllows()
    {
        var app = Load("content/page/new");

        Options(app).ShouldBe(["hero", "quote", "section"]);
        ChooseType(app, "section");
        Click(Actions(app), "Add Section");
        app.WaitForAssertion(() => Titles(app).ShouldBe(["Section 1"]));
        Click(Items(app)[0], "Add Card");
        app.WaitForAssertion(() => Items(app)[0].QuerySelectorAll(".nested-item").Length.ShouldBe(1));
        Items(app)[0].QuerySelector("[aria-label='Remove Card 1']").ShouldNotBeNull();
    }

    [Fact]
    public void AnErrorAtDepthThreeOpensItsBlocksAndShowsOnItsField()
    {
        var section = new JsonObject
        {
            ["type"] = "section",
            ["id"] = "section00001",
            ["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["id"] = "card00000001", ["link"] = new JsonObject { ["label"] = "x" } }),
        };
        var id = Page(Hero("aaaaaaaaaaaa", "A"), section);
        var app = Load($"content/page/{id:D}");
        Click(Actions(app), "Collapse all");
        app.WaitForAssertion(() => Items(app).ShouldAllBe(item => item.QuerySelector(".nested-body") == null));
        _content.NextWrite = _ => FakeContent.Invalid(("sections[1].cards[0].link.label", "The label is too short."), ("sections[1].id", "The block id is used twice."));

        Save(app);

        app.WaitForAssertion(() => Items(app)[1].QuerySelector(".nested-body").ShouldNotBeNull());
        Items(app)[0].QuerySelector(".nested-body").ShouldBeNull();
        var label = app.FindComponents<TextFieldEditor>().Single(editor => editor.Instance.Field.Name == "label");
        label.Instance.Errors.ShouldHaveSingleItem().Path.ShouldBe("label");
        label.Markup.ShouldContain("The label is too short.");
        Items(app)[1].TextContent.ShouldContain("The block id is used twice.");
        app.WaitForAssertion(() => Focused().ShouldBe(InputOf(label)));
    }

    [Fact]
    public void AComponentIsAGroupThatCollapsesAndARepeatableOneIsAListWhoseKeysAreNotSent()
    {
        var app = Load("content/page/new");
        var seo = FieldOf(app, "seo");
        seo.QuerySelector("input")!.Input("Home");
        seo.QuerySelector(".nested-toggle")!.Click();
        app.WaitForAssertion(() => FieldOf(app, "seo").QuerySelector("input").ShouldBeNull());
        FieldOf(app, "seo").QuerySelector(".nested-toggle")!.GetAttribute("aria-expanded").ShouldBe("false");

        Click(FieldOf(app, "links"), "Add Link");
        app.WaitForAssertion(() => FieldOf(app, "links").QuerySelectorAll(".nested-item").Length.ShouldBe(1));
        FieldOf(app, "links").QuerySelector("input")!.Input("First");
        Click(FieldOf(app, "links"), "Add Link");
        app.WaitForAssertion(() => FieldOf(app, "links").QuerySelectorAll(".nested-item").Length.ShouldBe(2));
        FieldOf(app, "links").QuerySelectorAll(".nested-item")[1].QuerySelector("input")!.Input("Second");
        FieldOf(app, "links").QuerySelector("[aria-label='Move Link 2 up']")!.Click();
        Input(app, "title").Input("Home");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var body = _content.Writes[0].Body;
        body["seo"]!.ToJsonString().ShouldBe("""{"metaTitle":"Home"}""");
        body["links"]!.ToJsonString().ShouldBe("""[{"label":"Second"},{"label":"First"}]""");
    }

    [Fact]
    public void ARelationAndAPluginFieldInABlockUseTheirEditors()
    {
        var ada = _content.Add("author", new JsonObject { ["name"] = "Ada Lovelace" });
        var hero = Hero("aaaaaaaaaaaa", "A");
        hero["author"] = new JsonObject { ["id"] = ada.ToString("D"), ["type"] = "author" };
        hero["score"] = 2;
        var id = Page(hero);
        Start();
        Services.GetRequiredService<FieldEditorRegistry>().AddLazy("rating", () => true, _ => Task.FromResult(typeof(RatingStub)));
        var app = Load($"content/page/{id:D}", started: true);

        app.WaitForAssertion(() => Items(app)[0].QuerySelector(".relation-field-name")!.TextContent.ShouldBe("Ada Lovelace"));
        Items(app)[0].QuerySelector("input.rating-stub")!.Change("5");
        IconButton(app, "Remove Ada Lovelace").Click();
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var saved = _content.Writes[0].Body["sections"]![0]!;
        saved["score"]!.GetValue<int>().ShouldBe(5);
        saved["author"].ShouldBeNull();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{12}$")]
    private static partial Regex IdPattern();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private Guid Page(params JsonObject[] sections) =>
        _content.Add("page", new JsonObject { ["title"] = "Home", ["sections"] = new JsonArray([.. sections]) });

    private IRenderedComponent<AdminApp> Load(string route, bool started = false)
    {
        if (!started)
        {
            Start();
        }

        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("[data-field='sections'] fieldset"));
        return app;
    }

    private static JsonObject Hero(string id, string heading) => new() { ["type"] = "hero", ["id"] = id, ["heading"] = heading };

    private static JsonObject Quote(string id, string text) => new() { ["type"] = "quote", ["id"] = id, ["text"] = text };

    private static JsonObject Page() => Type(
        "page",
        fields: [Field("title"), Blocks("sections", "hero", "quote", "section", "tower"), Component("seo", "seo"), Component("links", "link", multiple: true)]);

    private static JsonObject Blocks(string name, params string[] types) =>
        Field(name, "blocks", settings: new JsonObject { ["blocks"] = new JsonArray([.. types.Select(type => (JsonNode)type)]) });

    private static JsonObject Component(string name, string component, bool multiple = false)
    {
        var field = Field(name, "component", settings: new JsonObject { ["component"] = component });
        field["multiple"] = multiple;
        return field;
    }

    private static JsonObject Relation(string name)
    {
        var field = Field(name, "relation");
        field["relation"] = new JsonObject { ["target"] = "author" };
        return field;
    }

    private static void Save(IRenderedComponent<AdminApp> app) =>
        app.FindAll(".content-form-toolbar fluent-button").First(button => button.TextContent.Trim() == "Save").Click();

    private static IElement FieldOf(IRenderedComponent<AdminApp> app, string name) => app.Find($"main [data-field='{name}']");

    private static IElement Input(IRenderedComponent<AdminApp> app, string name) => app.Find($"[data-field='{name}'] input");

    // The items of the page's blocks, without those of the blocks they hold.
    private static List<IElement> Items(IRenderedComponent<AdminApp> app) =>
        [.. FieldOf(app, "sections").QuerySelector("ul.nested-items")!.Children];

    private static List<IElement> Heads(IRenderedComponent<AdminApp> app) => [.. Items(app).Select(item => item.QuerySelector(".nested-head")!)];

    private static List<string> Titles(IRenderedComponent<AdminApp> app) =>
        [.. Heads(app).Select(head => head.QuerySelector(".nested-toggle span")!.TextContent)];

    private static List<string?> Headings(IRenderedComponent<AdminApp> app) =>
        [.. Items(app).Select(item => item.QuerySelector(".nested-body input")?.GetAttribute("value"))];

    private static IElement Actions(IRenderedComponent<AdminApp> app) => FieldOf(app, "sections").QuerySelectorAll(".nested-actions")[^1];

    private static List<string> Options(IRenderedComponent<AdminApp> app) =>
        [.. Actions(app).QuerySelectorAll("select option").Select(option => option.GetAttribute("value")!)];

    private static void ChooseType(IRenderedComponent<AdminApp> app, string type)
    {
        var select = app.FindComponents<CmsSelect>().Single(found => found.Instance.Options.Any(option => option.Value == "hero"));
        select.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(type)).GetAwaiter().GetResult();
    }

    private static void Click(IElement container, string text) =>
        container.QuerySelectorAll("fluent-button").Last(button => button.TextContent.Trim() == text).Click();

    private static IElement IconButton(IRenderedComponent<AdminApp> app, string label) => app.Find($"main fluent-button[aria-label='{label}']");

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent)];

    private static List<string> Ids(JsonNode? sections) => [.. sections!.AsArray().Select(block => block!["id"]!.GetValue<string>())];

    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => ((ElementReference)call.Arguments[0]!).Id).LastOrDefault();

    private static NestedFieldEditor Sections(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<NestedFieldEditor>().Single(found => found.Instance.Field.Name == "sections").Instance;

    // The elements the page's blocks editor holds by position, read from the editor, since bUnit's markup does not name an
    // element once it has rendered again.
    private static string Toggle(IRenderedComponent<AdminApp> app, int position) =>
        ((Dictionary<int, ElementReference>)Private(Sections(app), "toggles"))[position].Id;

    private static string ButtonAt(IRenderedComponent<AdminApp> app, string buttons, int position)
    {
        var button = ((Dictionary<int, CmsButton>)Private(Sections(app), buttons))[position];
        var library = Private(button, "_button");
        return ((ElementReference)library.GetType().GetProperty("Element")!.GetValue(library)!).Id;
    }

    private static string InputOf(IRenderedComponent<TextFieldEditor> editor)
    {
        var input = Private(editor.Instance, "input");
        return ((ElementReference)input.GetType().BaseType!.GetProperty("Element", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(input)!).Id;
    }

    private static object Private(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    // A plugin's field type, whose editor loads with its admin half.
    private sealed class RatingStub : FieldEditorBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(1, "class", "rating-stub");
            builder.AddAttribute(2, "value", Value?.ToJsonString());
            builder.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => ChangeAsync(JsonValue.Create(int.Parse((string)e.Value!, System.Globalization.CultureInfo.InvariantCulture)))));
            builder.CloseElement();
        }
    }
}
