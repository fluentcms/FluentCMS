using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A URL in rich text with a scheme that is not allowed is refused on every write, wherever it is in the entry. One stored
// before that rule is left out of every read, and a write that only carries it on stores the value without it.
public sealed class RichTextLinkTests : IDisposable
{
    private const string Safe = "https://example.com/safe";
    private const string Script = "javascript:alert(1)";
    private const string Href = "body.content[0].content[0].marks[0].attrs.href";

    private static readonly string[] Cards = ["card"];

    private readonly ServiceDatabase _database = new(schema: Model());

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddComponent("box", component => component.Field("note", "richText"))
        .AddBlock("card", block => block.Field("label", "text").Field("note", "richText"))
        .AddCollection("article", type => type
            .Field("title", "text")
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("box", "component", field => field.Setting(FieldSettings.Component, "box"))
            .Field("boxes", "component", field => field.Multiple().Setting(FieldSettings.Component, "box"))
            .Field("body", "richText"))
        .Build();

    public void Dispose() => _database.Dispose();

    private static string Link(string href) => JsonSerializer.Serialize(new { type = "link", attrs = new { href } });

    private static string Linked(string href, string marks = "") =>
        $$"""{"type":"text","text":"Click","marks":[{{marks}}{{Link(href)}}]}""";

    private static RichText Paragraph(params string[] inlines) =>
        new("""{"type":"doc","content":[{"type":"paragraph","content":[""" + string.Join(",", inlines) + "]}]}");

    private static string Url(string url) => JsonSerializer.Serialize(url);

    public static TheoryData<string> UnsafeHrefs() => new()
    {
        Script,
        "JavaScript:alert(1)",
        "  javascript:alert(1)",
        "java\tscript:alert(1)",
        "java\nscript:alert(1)",
        "java​script:alert(1)",
        "‮javascript:alert(1)",
        "java\\script:alert(1)",
        "&#106;avascript:alert(1)",
        "javascript&colon;alert(1)",
        "vbscript:msgbox(1)",
        "data:text/html,<script>alert(1)</script>",
        "sms:+15551234567",
    };

    // A percent-encoded colon or a backslash makes no scheme: a browser reads these as paths or as a protocol-relative URL.
    public static TheoryData<string> SafeHrefs() => new()
    {
        Safe,
        "HTTP://example.com",
        "mailto:someone@example.com",
        "tel:+15551234567",
        "/about",
        "about/team",
        "../up",
        "#section",
        "?page=2",
        "//cdn.example.com/file",
        "\\\\cdn.example.com\\file",
        "javascript%3Aalert(1)",
        "https://example.com/a?b=javascript:c",
    };

    // Each place a URL can be, and the path of the error. Every place holds the text "Click".
    public static TheoryData<string, string> Places() => new()
    {
        { "text", Href },
        { "second mark", "body.content[0].content[0].marks[1].attrs.href" },
        { "list", "body.content[0].content[0].content[0].content[0].marks[0].attrs.href" },
        { "blockquote", "body.content[0].content[0].content[0].marks[0].attrs.href" },
        { "inline block", "body.content[0].content[1].marks[0].attrs.href" },
        { "inline block data", "body.content[0].content[1].attrs.data.note.content[0].content[0].marks[0].attrs.href" },
        { "unknown inline block data", "body.content[0].content[1].attrs.data.links[0].href" },
        { "media src", "body.content[0].content[1].attrs.src" },
        { "entry link href", "body.content[0].content[0].marks[0].attrs.href" },
        { "block", "cards[0].note.content[0].content[0].marks[0].attrs.href" },
        { "component", "box.note.content[0].content[0].marks[0].attrs.href" },
        { "repeated component", "boxes[0].note.content[0].content[0].marks[0].attrs.href" },
    };

    public static TheoryData<string> PlaceNames()
    {
        var names = new TheoryData<string>();
        foreach (var row in Places())
        {
            names.Add(row.Data.Item1);
        }

        return names;
    }

    private static ContentDocument Placed(string place, string href)
    {
        var entry = new ContentDocument { ["title"] = "T" };
        const string Click = """{"type":"text","text":"Click"}""";
        switch (place)
        {
            case "text":
                entry["body"] = Paragraph(Linked(href));
                break;
            case "second mark":
                entry["body"] = Paragraph(Linked(href, marks: """{"type":"bold"},"""));
                break;
            case "list":
                entry["body"] = new RichText("""{"type":"doc","content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[""" + Linked(href) + "]}]}]}]}");
                break;
            case "blockquote":
                entry["body"] = new RichText("""{"type":"doc","content":[{"type":"blockquote","content":[{"type":"paragraph","content":[""" + Linked(href) + "]}]}]}");
                break;
            case "inline block":
                entry["body"] = Paragraph(Click, """{"type":"inlineBlock","attrs":{"blockType":"card","data":{"label":"x"}},"marks":[""" + Link(href) + "]}");
                break;
            case "inline block data":
                entry["body"] = Paragraph(Click, """{"type":"inlineBlock","attrs":{"blockType":"card","data":{"note":""" + Paragraph(Linked(href)).Json + "}}}");
                break;
            case "unknown inline block data":
                entry["body"] = Paragraph(Click, """{"type":"inlineBlock","attrs":{"blockType":"gallery","data":{"links":[{"href":""" + Url(href) + "}]}}}");
                break;
            case "media src":
                entry["body"] = Paragraph(Click, """{"type":"media","attrs":{"mediaId":"11111111-1111-1111-1111-111111111111","src":""" + Url(href) + "}}");
                break;
            case "entry link href":
                entry["body"] = Paragraph("""{"type":"text","text":"Click","marks":[{"type":"entryLink","attrs":{"entryId":"11111111-1111-1111-1111-111111111111","entryType":"article","href":""" + Url(href) + "}}]}");
                break;
            case "block":
                entry["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["note"] = JsonNode.Parse(Paragraph(Linked(href)).Json) });
                break;
            case "component":
                entry["box"] = new ContentDocument { ["note"] = Paragraph(Linked(href)) };
                break;
            case "repeated component":
                entry["boxes"] = new JsonArray(new JsonObject { ["note"] = JsonNode.Parse(Paragraph(Linked(href)).Json) });
                break;
        }

        return entry;
    }

    [Theory]
    [MemberData(nameof(UnsafeHrefs))]
    public async Task AnUnsafeHrefIsRefusedAtItsPath(string href)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Placed("text", href)))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(Href);
        error.Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);
    }

    [Theory]
    [MemberData(nameof(Places))]
    public async Task AnUnsafeUrlIsRefusedWhereverItIs(string place, string path)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Placed(place, Script)))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(path);
        error.Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);
    }

    [Theory]
    [MemberData(nameof(SafeHrefs))]
    public async Task ASafeHrefIsKept(string href)
    {
        var created = await _database.CreateAsync(Editor, Placed("text", href));

        LinkOf(created).ShouldBe(href);
    }

    [Theory]
    [MemberData(nameof(PlaceNames))]
    public async Task ASafeUrlIsKeptWhereverItIs(string place)
    {
        await _database.CreateAsync(Editor, Placed(place, Safe));

        // As stored: a read drops an entry link to an entry that does not exist.
        JsonSerializer.Serialize(_database.Read(context => context.Rows("cms_article").ToList())).ShouldContain(Safe);
    }

    [Fact]
    public async Task AnAllowedSchemeIsTakenFromTheOptions()
    {
        using var database = new ServiceDatabase(cms => cms.Services.PostConfigure<RichTextOptions>(options => options.LinkSchemes.Add("sms")), Model());

        var created = await database.CreateAsync(Editor, Placed("text", "sms:+15551234567"));
        var error = (await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Placed("text", Safe)))).Errors.ShouldHaveSingleItem();

        LinkOf(created).ShouldBe("sms:+15551234567");
        error.Path.ShouldBe(Href);
    }

    [Fact]
    public async Task AnUpdateAndAPatchRefuseAnUnsafeHref()
    {
        var created = await _database.CreateAsync(Editor, Placed("text", Safe));
        var id = IdOf(created);

        var update = Placed("block", Script);
        update["body"] = Paragraph(Linked(Safe));
        (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(Editor, id, update, VersionOf(created)))).Errors
            .ShouldHaveSingleItem().Path.ShouldBe("cards[0].note.content[0].content[0].marks[0].attrs.href");

        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, id, new() { ["body"] = Paragraph(Linked(Script)) }, VersionOf(created)))).Errors
            .ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);

        LinkOf(await _database.GetAsync(Editor, id)).ShouldBe(Safe);
    }

    [Fact]
    public async Task AHookCannotSetAnUnsafeHref()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeCreateHook<ContentDocument>>(new LinkingHook()), Model());

        var error = (await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Placed("text", Safe)))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(Href);
        error.Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);
    }

    [Theory]
    [MemberData(nameof(PlaceNames))]
    public async Task AStoredUnsafeUrlIsLeftOutOfEveryRead(string place)
    {
        var id = IdOf(await _database.CreateAsync(Editor, Placed(place, Safe)));
        await _database.PublishAsync(Editor, id);
        StoreUnsafe();

        foreach (var read in new[]
        {
            await _database.GetAsync(Editor, id),
            await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }),
            (await _database.QueryAsync(Editor)).Items.ShouldHaveSingleItem(),
            await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().GetVersionAsync("article", id, 1, null, Token)),
        })
        {
            var json = Json(read);
            json.ShouldNotContain("javascript");
            json.ShouldNotContain(Safe);
            json.ShouldContain("Click");
        }
    }

    [Fact]
    public async Task AStoredUnsafeHrefIsRenderedAsBefore()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Placed("text", Safe)));
        await _database.PublishAsync(Editor, id);
        StoreUnsafe();

        var html = (string)(await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published, RichTextFormat = "html" }))!["body"]!;
        var markdown = (string)(await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published, RichTextFormat = "markdown" }))!["body"]!;

        html.ShouldBe("<p>Click</p>");
        markdown.ShouldBe("Click");
    }

    // A patch of another field and a restore carry the stored value on and store it without the link. A publish copies the
    // draft row as it is, so it succeeds and its row keeps the link, which no read returns.
    [Fact]
    public async Task AWriteThatCarriesAStoredUnsafeHrefIsNotRefused()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Placed("text", Safe)));
        await _database.PublishAsync(Editor, id);
        StoreUnsafe();

        var patched = await _database.PatchAsync(Editor, id, new() { ["title"] = "Again" }, 1);
        ((string)_database.Row("cms_article", id)["body"]).ShouldNotContain("javascript");

        StoreUnsafe(Paragraph(Linked(Script)).Json);
        await _database.PublishAsync(Editor, id);
        var published = await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published });

        StoreUnsafe(Paragraph(Linked(Script)).Json);
        await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().RestoreVersionAsync("article", id, 1, null, Token));

        VersionOf(patched).ShouldBe(2);
        Json(published).ShouldNotContain("javascript");
        ((string)_database.Row("cms_article", id)["body"]).ShouldNotContain("javascript");
    }

    // Puts javascript: in place of the safe URL in every row and version, as data stored before the rule would have it, or
    // stores the body given in every row.
    private void StoreUnsafe(string? body = null) => _database.Read(context =>
    {
        foreach (var row in context.Rows("cms_article").ToList())
        {
            foreach (var key in row.Keys.ToList())
            {
                if (body is not null && key == "body")
                {
                    row[key] = body;
                }
                else if (row[key] is string text && text.Contains(Safe, StringComparison.Ordinal))
                {
                    row[key] = text.Replace(Safe, Script, StringComparison.Ordinal);
                }
            }
        }

        foreach (var version in context.Versions.ToList())
        {
            version.Snapshot = version.Snapshot.Replace(Safe, Script, StringComparison.Ordinal);
        }

        return context.SaveChanges();
    });

    private static string Json(ContentDocument? entry) => JsonSerializer.Serialize(entry);

    private static string? LinkOf(ContentDocument? entry) =>
        JsonNode.Parse(((RichText)entry!["body"]!).Json)!["content"]![0]!["content"]![0]!["marks"]!.AsArray()
            .Single(mark => (string?)mark!["type"] == "link")!["attrs"]!["href"]!.GetValue<string>();

    private sealed class LinkingHook : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["body"] = Paragraph(Linked(Script));
            return ValueTask.CompletedTask;
        }
    }
}
