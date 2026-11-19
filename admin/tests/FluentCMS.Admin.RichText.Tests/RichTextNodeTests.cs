using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Pickers;
using FluentCMS.Admin.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.RichText.Tests;

// Links to entries, media embeds and inline blocks (A-25): what the editor asks the pickers, the API and the block form for,
// and what it then tells the module. The module's own part, the document it writes, is covered by its schema test and the
// end-to-end case.
public sealed class RichTextNodeTests : BunitContext
{
    private const string Document = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]}]}""";
    private static readonly Guid Article = Guid.Parse("0b9f4a52-7c1e-4d0a-9a43-5e1f6b2c8d70");
    private static readonly Guid Asset = Guid.Parse("5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a");

    private readonly FakeEditorRuntime runtime = new();
    private readonly FakeSchema schema = new();
    private readonly FakeRelationPicker relations = new();
    private readonly FakeMediaPicker media = new();
    private readonly FakePlugins plugins;
    private readonly FakeApi api = new();
    private readonly List<JsonNode?> changes = [];

    public RichTextNodeTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        plugins = new FakePlugins(media);
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IJSRuntime>(runtime);
        Services.AddSingleton<IAdminSchema>(schema);
        Services.AddSingleton<IRelationPicker>(relations);
        Services.AddSingleton<IAdminPluginServices>(plugins);
        Services.AddSingleton<IManagementApi>(api);
        Services.AddSingleton(new AdminConfiguration { ApiBaseUrl = new Uri("https://api.example.com/") });
        Services.AddSingleton(new BlockFormRegistration(typeof(FakeBlockForm)));
    }

    private FakeEditor Editor => runtime.Editor;

    [Fact]
    public void TheToolbarHasTheEntryLinkMediaAndBlockButtonsWithTheirShortcuts()
    {
        var cut = Edit(blocks: ["note"]);

        var buttons = cut.FindAll("[role=toolbar] button");
        buttons.Count.ShouldBe(RichTextFieldEditor.Buttons.Length);
        cut.Find("button[data-command=entryLink]").GetAttribute("aria-label").ShouldBe("Link to an entry");
        cut.Find("button[data-command=entryLink]").GetAttribute("aria-keyshortcuts").ShouldBe("Control+Alt+K");
        cut.Find("button[data-command=entryLink]").GetAttribute("aria-pressed").ShouldBe("false");
        cut.Find("button[data-command=media]").GetAttribute("title").ShouldBe("Embed media (Ctrl+Alt+M)");
        cut.Find("button[data-command=inlineBlock]").GetAttribute("aria-keyshortcuts").ShouldBe("Control+Alt+B");
    }

    [Fact]
    public void AFieldWithoutBlocksHasNoBlockButton()
    {
        var cut = Edit();

        cut.FindAll("button[data-command=inlineBlock]").ShouldBeEmpty();
        cut.FindAll("[role=toolbar] button").Count.ShouldBe(RichTextFieldEditor.Buttons.Length - 1);
    }

    [Fact]
    public void TheModuleIsGivenTheNamesOfTheBlockTypesAndThePlaceholders()
    {
        Edit(blocks: ["note"]);

        var options = (RichTextFieldEditor.EditorOptions)runtime.Module.Created![4]!;
        options.BlockLabels["note"].ShouldBe("Note");
        options.Labels.MediaUnavailable.ShouldBe("Media is not enabled in this site");
        options.Labels.MediaMissing.ShouldBe("Missing media: the asset no longer exists");
        options.BlockFields!.Blocks["card"].ShouldBe(new Dictionary<string, string> { ["links"] = "blocks", ["body"] = "richText", ["meta"] = "component:meta" });
        options.BlockFields.Components["meta"].ShouldBe(new Dictionary<string, string> { ["more"] = "blocks" });
    }

    [Fact]
    public async Task AnEntryLinkIsChosenWithTheRelationPickerAndNamedByItsTitle()
    {
        var cut = Edit();
        relations.Answer = [new ContentEntry { Id = Article, Fields = new JsonObject { ["title"] = "Spring" } }];

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("entryLink"));
        cut.Find("[role=group][aria-label='Link to an entry'] select").ShouldNotBeNull();
        cut.Find("select").Change("article");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Choose an entry").Click());

        cut.WaitForAssertion(() => Editor.CallsOf("setEntryLink").Single().ShouldBe([Article.ToString("D"), "article", "Spring"]));
        relations.Requests.Single().Type.ShouldBe("article");
        relations.Requests.Single().Locale.ShouldBe("en");
        Editor.CallsOf("setEntryTitle").Single().ShouldBe(["article", Article.ToString("D"), "Article: Spring"]);
        cut.FindAll("[role=group][aria-label='Link to an entry']").ShouldBeEmpty();
    }

    [Fact]
    public async Task AnExistingEntryLinkShowsItsEntryAndCanBeRemoved()
    {
        var cut = Edit();
        api.Answers["content/article"] = $$"""{"items":[{"id":"{{Article}}","title":"Spring"}]}""";
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([], [new EntryReference(Article.ToString("D"), "article")]));
        Editor.EntryLink = new EntryReference(Article.ToString("D"), "article");

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("entryLink"));

        cut.Find(".rich-text-entry-current").TextContent.ShouldBe("Links to Article: Spring");
        api.Requests.Single().ShouldStartWith($"content/article?filter[id][in]={Article:D}&fields=id,title");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Remove entry link").Click());
        Editor.CallsOf("unsetEntryLink").Count().ShouldBe(1);
    }

    [Fact]
    public async Task ALinkToAnEntryThatIsGoneOrOfATypeTheUserMayNotReadSaysSo()
    {
        var cut = Edit();
        api.Answers["content/article"] = """{"items":[]}""";

        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([], [new EntryReference(Article.ToString("D"), "article"), new EntryReference(Article.ToString("D"), "secret")]));

        Editor.CallsOf("setEntryTitle").Select(call => (string)call[2]!).ShouldBe(
            ["Article: an entry that cannot be read or no longer exists", "An entry of secret, a type you may not read"], ignoreOrder: true);
    }

    [Fact]
    public async Task MediaIsChosenWithTheMediaHalfsPickerAndShownFromItsUrl()
    {
        var cut = Edit();
        media.Answer = [new MediaPickerItem { Id = Asset, FileName = "sun.png", MimeType = "image/png", ThumbnailUrl = "/media/sun-thumb.png", Alt = "The sun" }];

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("media"));

        cut.WaitForAssertion(() => Editor.CallsOf("insertMedia").Single().ShouldBe([Asset.ToString("D")]));
        plugins.Loads.ShouldBe(1);
        var shown = (MediaPreview)Editor.CallsOf("setMedia").Single()[1]!;
        shown.ShouldBe(new MediaPreview(MediaPreview.Ready, "https://api.example.com/media/sun-thumb.png", "sun.png", "The sun"));
        Editor.CallsOf("focus").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task WithoutTheMediaPluginNothingIsEmbeddedAndEmbedsArePlaceholders()
    {
        plugins.Disabled = true;
        api.PluginDisabled = true;
        var cut = Edit();

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("media"));
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));

        cut.Find("[role=status]").TextContent.ShouldBe("Media is not enabled in this site, so nothing can be embedded.");
        Editor.CallsOf("insertMedia").ShouldBeEmpty();
        ((MediaPreview)Editor.CallsOf("setMedia").Single()[1]!).State.ShouldBe(MediaPreview.Unavailable);
    }

    [Fact]
    public async Task PreviewsAreReadWithoutLoadingTheMediaHalfAndARefusedReadIsShownAndNotRepeated()
    {
        var cut = Edit();
        api.Refused.Add($"media/{Asset:D}");

        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));

        ((MediaPreview)Editor.CallsOf("setMedia").Single()[1]!).ShouldBe(MediaPreview.Failed);
        api.Requests.Count.ShouldBe(1);
        plugins.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task AnotherContentLocaleReadsTheTitlesAndPreviewsAgain()
    {
        var cut = Edit();
        api.Answers[$"media/{Asset:D}"] = """{"fileName":"sun.png","mimeType":"image/png","urls":{"original":"/sun.png"}}""";
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));

        cut.Render(parameters => parameters.Add(editor => editor.ContentLocale, "fr"));
        cut.WaitForAssertion(() => Editor.CallsOf("referAgain").Count().ShouldBe(1));
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));

        api.Locales.ShouldBe(["en", "fr"]);
    }

    [Fact]
    public async Task AnEmbedIsReadForItsPreviewAndOneThatIsGoneIsAPlaceholder()
    {
        var gone = Guid.NewGuid();
        var cut = Edit();
        api.Answers[$"media/{Asset:D}"] = """{"id":"x","fileName":"sun.png","mimeType":"image/png","urls":{"original":"https://cdn.example.com/sun.png","thumbnail":"javascript:alert(1)"}}""";

        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D"), gone.ToString("D")], []));
        await cut.InvokeAsync(() => cut.Instance.OnReferencesAsync([Asset.ToString("D")], []));

        var shown = Editor.CallsOf("setMedia").ToDictionary(call => (string)call[0]!, call => (MediaPreview)call[1]!);
        shown[Asset.ToString("D")].State.ShouldBe(MediaPreview.Ready);
        shown[Asset.ToString("D")].Preview.ShouldBeNull();
        shown[gone.ToString("D")].ShouldBe(MediaPreview.Gone);
        api.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ASelectedMediaOpensItsDialogWhereItsAltTextIsSetOrItIsRemoved()
    {
        var cut = Edit();
        Editor.Kept = new RichTextFieldEditor.KeptNode("media", $$"""{"mediaId":"{{Asset}}","alt":"Old"}""", ".content[1]");

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());

        var input = cut.Find(".rich-text-media-dialog input");
        input.GetAttribute("value").ShouldBe("Old");
        input.Input("A bright sun");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Done").Click());
        JsonNode.DeepEquals(JsonNode.Parse((string)Editor.CallsOf("updateKept").Single()[0]!), JsonNode.Parse($$"""{"mediaId":"{{Asset}}","alt":"A bright sun"}""")).ShouldBeTrue();

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        cut.Find(".rich-text-media-dialog input").Input("  ");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Done").Click());
        JsonNode.DeepEquals(JsonNode.Parse((string)Editor.CallsOf("updateKept").Last()[0]!), JsonNode.Parse($$"""{"mediaId":"{{Asset}}","alt":"Old"}""")).ShouldBeFalse();
        JsonNode.Parse((string)Editor.CallsOf("updateKept").Last()[0]!)!.AsObject().ContainsKey("alt").ShouldBeFalse();

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Remove").Click());
        Editor.CallsOf("removeKept").Count().ShouldBe(1);
    }

    [Fact]
    public async Task ReplacingAnEmbedPicksAnotherAssetAndDropsTheOldAltText()
    {
        var cut = Edit();
        var other = Guid.NewGuid();
        Editor.Kept = new RichTextFieldEditor.KeptNode("media", $$"""{"mediaId":"{{Asset}}","alt":"Old"}""", ".content[1]");
        media.Answer = [new MediaPickerItem { Id = other, FileName = "moon.png", MimeType = "image/png" }];

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Replace").Click());

        cut.WaitForAssertion(() => Editor.CallsOf("updateKept").Single()[0].ShouldBe($$"""{"mediaId":"{{other:D}}"}"""));
        media.Requests.Single().Selected.ShouldBe([Asset]);

        // The old asset's alternative text is not written on the new one.
        cut.Find(".rich-text-media-dialog input").GetAttribute("value").ShouldBeNullOrEmpty();
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Done").Click());
        Editor.CallsOf("updateKept").Last()[0].ShouldBe($$"""{"mediaId":"{{other:D}}"}""");
    }

    [Fact]
    public async Task ANewInlineBlockOfTheFieldsOneTypeIsInsertedWithItsDefaultsAndOpenInItsForm()
    {
        var cut = Edit(blocks: ["note"]);
        Editor.Inserted = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"note","id":"Nq8vB2xK9pLm","data":{"level":"info"}}""", ".content[0].content[1]");

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("inlineBlock"));

        Editor.CallsOf("insertBlock").Single().ShouldBe(["note", """{"level":"info"}"""]);
        var form = cut.FindComponent<FakeBlockForm>().Instance;
        form.BlockType.Name.ShouldBe("note");
        form.Path.ShouldBe("body.content[0].content[1].attrs.data");
        form.Value!["level"]!.GetValue<string>().ShouldBe("info");

        await cut.InvokeAsync(() => form.ValueChanged.InvokeAsync(new JsonObject { ["level"] = "info", ["text"] = "Typed" }));

        JsonNode.DeepEquals(JsonNode.Parse((string)Editor.CallsOf("updateKept").Single()[0]!),
            JsonNode.Parse("""{"blockType":"note","id":"Nq8vB2xK9pLm","data":{"level":"info","text":"Typed"}}""")).ShouldBeTrue();
    }

    [Fact]
    public async Task OfSeveralBlockTypesOneIsChosenFirst()
    {
        var cut = Edit(blocks: ["note", "quote"]);
        Editor.Inserted = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"quote","id":"abc","data":{}}""", ".content[1]");

        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("inlineBlock"));
        cut.Find(".rich-text-block-dialog select").Change("quote");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Insert").Click());

        Editor.CallsOf("insertBlock").Single().ShouldBe(["quote", "{}"]);
        cut.FindComponent<FakeBlockForm>().Instance.BlockType.Name.ShouldBe("quote");
    }

    [Fact]
    public async Task ASelectedInlineBlockIsEditedDuplicatedAndRemovedFromItsForm()
    {
        var cut = Edit(blocks: ["note"]);
        Editor.Kept = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"note","id":"first","data":{"text":"One"}}""", ".content[2]");
        Editor.Inserted = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"note","id":"copy","data":{"text":"One"}}""", ".content[3]");

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        cut.FindComponent<FakeBlockForm>().Instance.Value!["text"]!.GetValue<string>().ShouldBe("One");
        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Duplicate").Click());

        Editor.CallsOf("duplicateKept").Count().ShouldBe(1);
        cut.FindComponent<FakeBlockForm>().Instance.Path.ShouldBe("body.content[3].attrs.data");
        cut.Find("[role=status]").TextContent.ShouldBe("The block was duplicated, and the copy is open.");

        await cut.InvokeAsync(() => cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Remove").Click());
        Editor.CallsOf("removeKept").Count().ShouldBe(1);
        Editor.CallsOf("insertBlock").ShouldBeEmpty();
    }

    [Fact]
    public async Task ASaveTakesTheChangeTheOpenBlockFormHoldsBack()
    {
        var cut = Edit(blocks: ["note"]);
        Editor.Kept = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"note","id":"first","data":{"text":"One"}}""", ".content[2]");
        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        cut.FindComponent<FakeBlockForm>().Instance.Held = new JsonObject { ["text"] = "Held back" };

        await cut.InvokeAsync(() => cut.Instance.FlushAsync().AsTask());

        JsonNode.Parse((string)Editor.CallsOf("updateKept").Single()[0]!)!["data"]!["text"]!.GetValue<string>().ShouldBe("Held back");
        Editor.CallsOf("flush").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ClosingTheBlockDialogByEscapeTakesTheChangeItsFormHoldsBack()
    {
        var cut = Edit(blocks: ["note"]);
        Editor.Kept = new RichTextFieldEditor.KeptNode("inlineBlock", """{"blockType":"note","id":"first","data":{"text":"One"}}""", ".content[2]");
        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        cut.FindComponent<FakeBlockForm>().Instance.Held = new JsonObject { ["text"] = "Held back" };

        // What the dialog raises when Escape or a click outside closes it.
        var dialog = cut.FindComponents<CmsDialog>().Single(found => found.Instance.Title == "Block: Note").Instance;
        await cut.InvokeAsync(() => dialog.OpenChanged.InvokeAsync(false));

        JsonNode.Parse((string)Editor.CallsOf("updateKept").Single()[0]!)!["data"]!["text"]!.GetValue<string>().ShouldBe("Held back");
    }

    [Fact]
    public async Task AReadOnlyFieldOpensNothing()
    {
        var cut = Edit(blocks: ["note"], readOnly: true);
        Editor.Kept = new RichTextFieldEditor.KeptNode("media", $$"""{"mediaId":"{{Asset}}"}""", ".content[1]");

        await cut.InvokeAsync(() => cut.Instance.OnKeptRequestedAsync());
        await cut.InvokeAsync(() => cut.Instance.OnCommandRequestedAsync("media"));

        cut.FindAll(".rich-text-media-dialog").ShouldBeEmpty();
        media.Requests.ShouldBeEmpty();
    }

    private IRenderedComponent<RichTextFieldEditor> Edit(string[]? blocks = null, bool readOnly = false)
    {
        var cut = Render<RichTextFieldEditor>(parameters => parameters
            .Add(editor => editor.Field, new SchemaFieldDescription
            {
                Name = "body",
                Type = "richText",
                Validation = new FieldValidationDescription(),
                Settings = blocks is null ? [] : new JsonObject { ["blocks"] = new JsonArray([.. blocks.Select(name => (JsonNode)name)]) },
            })
            .Add(editor => editor.Value, JsonNode.Parse(Document))
            .Add(editor => editor.ReadOnly, readOnly)
            .Add(editor => editor.ContentLocale, "en")
            .Add(editor => editor.ValueChanged, EventCallback.Factory.Create<JsonNode?>(this, changed => changes.Add(changed))));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        cut.WaitForAssertion(() => cut.Instance.ShouldNotBeNull());
        return cut;
    }

    private sealed class FakeSchema : IAdminSchema
    {
        private static SchemaFieldDescription Text(string name, JsonNode? defaultValue = null) =>
            new() { Name = name, Type = "text", Validation = new FieldValidationDescription(), DefaultValue = defaultValue };

        private static SchemaFieldDescription Field(string name, string type, JsonObject? settings = null) =>
            new() { Name = name, Type = type, Validation = new FieldValidationDescription(), Settings = settings ?? [] };

        public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SchemaDescription
        {
            Types =
            [
                new SchemaTypeDescription { Name = "article", Kind = "collection", DisplayName = "Article", TitleField = "title", Fields = [Text("title")] },
                new SchemaTypeDescription { Name = "author", Kind = "collection", Fields = [Text("name")] },
            ],
            Components = [new SchemaTypeDescription { Name = "meta", Kind = "component", Fields = [Field("more", "blocks"), Text("note")] }],
            Blocks =
            [
                new SchemaTypeDescription { Name = "note", Kind = "block", DisplayName = "Note", Fields = [Text("level", "info"), Text("text")] },
                new SchemaTypeDescription { Name = "quote", Kind = "block", Fields = [Text("text")] },
                new SchemaTypeDescription
                {
                    Name = "card",
                    Kind = "block",
                    Fields =
                    [
                        Text("title"),
                        Field("tags", "relation"),
                        Field("images", "media"),
                        Field("links", "blocks"),
                        Field("body", "richText"),
                        Field("meta", "component", new JsonObject { ["component"] = "meta" }),
                    ],
                },
            ],
        });
    }

    private sealed class FakeRelationPicker : IRelationPicker
    {
        public List<RelationPickerRequest> Requests { get; } = [];

        public IReadOnlyList<ContentEntry>? Answer { get; set; }

        public Task<IReadOnlyList<ContentEntry>?> PickAsync(RelationPickerRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeMediaPicker : IMediaPicker
    {
        public List<MediaPickerRequest> Requests { get; } = [];

        public IReadOnlyList<MediaPickerItem>? Answer { get; set; }

        public Task<IReadOnlyList<MediaPickerItem>?> PickAsync(MediaPickerRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakePlugins(IMediaPicker picker) : IAdminPluginServices
    {
        public bool Disabled { get; set; }

        public int Loads { get; private set; }

        public IServiceProvider ProviderOf(Type type) => throw new NotSupportedException();

        public ValueTask<TService?> FindAsync<TService>(CancellationToken cancellationToken = default)
            where TService : class
        {
            Loads++;
            return ValueTask.FromResult(Disabled ? null : picker as TService);
        }
    }

    private sealed class FakeApi : IManagementApi
    {
        public List<string> Requests { get; } = [];

        public List<string?> Locales { get; } = [];

        // Answers by the path before its query; a path without an answer is not found.
        public Dictionary<string, string> Answers { get; } = new(StringComparer.Ordinal);

        // Paths the caller may not read.
        public HashSet<string> Refused { get; } = new(StringComparer.Ordinal);

        // Whether the media plugin is not enabled in the site, which the server answers on its routes with 404 and FCMS1550.
        public bool PluginDisabled { get; set; }

        public Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
        {
            Requests.Add(request.Path);
            Locales.Add(request.Locale);
            var path = request.Path.Split('?')[0];
            if (PluginDisabled && path.StartsWith("media/", StringComparison.Ordinal))
            {
                return Task.FromException<ApiResponse<T>>(new ApiProblemException(new ApiProblem { Status = 404, Code = "FCMS1550", Title = "Plugin not enabled" }));
            }

            if (Refused.Contains(path))
            {
                return Task.FromException<ApiResponse<T>>(new ApiProblemException(new ApiProblem { Status = 403, Title = "Forbidden" }));
            }

            return Answers.TryGetValue(path, out var json)
                ? Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, null, JsonSerializer.Deserialize(json, answer)!))
                : Task.FromException<ApiResponse<T>>(new ApiProblemException(new ApiProblem { Status = 404, Title = "Not found" }));
        }

        public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

// The block form as the admin's BlockFormRegistration gives it, which records its parameters and holds back a change until flushed.
public sealed class FakeBlockForm : BlockFormBase
{
    public JsonObject? Held { get; set; }

    public override async ValueTask FlushAsync()
    {
        if (Held is { } held)
        {
            Held = null;
            await ValueChanged.InvokeAsync(held);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "fake-block-form");
        builder.AddContent(2, BlockType.Name);
        builder.CloseElement();
    }
}
