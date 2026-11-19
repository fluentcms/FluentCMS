using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A rich text link whose href is not a safe URL is refused on every write, wherever it is in the entry. One stored before
// that rule reads without it.
public sealed class RichTextLinkTests : IDisposable
{
    private const string Safe = "https://example.com/safe";
    private const string Script = "javascript:alert(1)";

    private static readonly string[] Cards = ["card"];

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddComponent("box", component => component.Field("note", "richText"))
        .AddBlock("card", block => block.Field("label", "text").Field("note", "richText"))
        .AddCollection("article", type => type
            .Field("title", "text")
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("box", "component", field => field.Setting(FieldSettings.Component, "box"))
            .Field("body", "richText"))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model());

    public void Dispose() => _database.Dispose();

    private static string Link(string href) => JsonSerializer.Serialize(new { type = "link", attrs = new { href } });

    private static string Linked(string href, string text = "Click", string marks = "") =>
        $$"""{"type":"text","text":"{{text}}","marks":[{{marks}}{{Link(href)}}]}""";

    private static RichText Paragraph(params string[] inlines) =>
        new("""{"type":"doc","content":[{"type":"paragraph","content":[""" + string.Join(",", inlines) + "]}]}");

    private static JsonNode? Note(RichText document) => JsonNode.Parse(document.Json);

    public static TheoryData<string> UnsafeHrefs() => new()
    {
        Script,
        "JavaScript:alert(1)",
        "  javascript:alert(1)",
        "java\tscript:alert(1)",
        "java\nscript:alert(1)",
        "&#106;avascript:alert(1)",
        "javascript&colon;alert(1)",
        "vbscript:msgbox(1)",
        "data:text/html,<script>alert(1)</script>",
    };

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
        "https://example.com/a?b=javascript:c",
    };

    // Each place a link can be: the field that holds it, and the path of its href.
    public static TheoryData<string, string> Places() => new()
    {
        { "text", "body.content[0].content[0].marks[0].attrs.href" },
        { "second mark", "body.content[0].content[0].marks[1].attrs.href" },
        { "list", "body.content[0].content[0].content[0].content[0].marks[0].attrs.href" },
        { "inline block", "body.content[0].content[1].marks[0].attrs.href" },
        { "inline block data", "body.content[0].content[1].attrs.data.note.content[0].content[0].marks[0].attrs.href" },
        { "block", "cards[0].note.content[0].content[0].marks[0].attrs.href" },
        { "component", "box.note.content[0].content[0].marks[0].attrs.href" },
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
            case "inline block":
                entry["body"] = Paragraph("""{"type":"text","text":"Click"}""", """{"type":"inlineBlock","attrs":{"blockType":"card","data":{"label":"x"}},"marks":[""" + Link(href) + "]}");
                break;
            case "inline block data":
                entry["body"] = Paragraph("""{"type":"text","text":"Click"}""", """{"type":"inlineBlock","attrs":{"blockType":"card","data":{"note":""" + Paragraph(Linked(href)).Json + "}}}");
                break;
            case "block":
                entry["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["note"] = Note(Paragraph(Linked(href))) });
                break;
            case "component":
                entry["box"] = new ContentDocument { ["note"] = Paragraph(Linked(href)) };
                break;
        }

        return entry;
    }

    [Theory]
    [MemberData(nameof(UnsafeHrefs))]
    public async Task AnUnsafeHrefIsRefusedAtItsPath(string href)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Placed("text", href)))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe("body.content[0].content[0].marks[0].attrs.href");
        error.Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);
    }

    [Theory]
    [MemberData(nameof(Places))]
    public async Task AnUnsafeHrefIsRefusedWhereverItIs(string place, string path)
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

        Href(created).ShouldBe(href);
    }

    [Theory]
    [MemberData(nameof(PlaceNames))]
    public async Task ASafeHrefIsKeptWhereverItIs(string place)
    {
        var id = IdOf(await _database.CreateAsync(Editor, Placed(place, Safe)));

        Json(await _database.GetAsync(Editor, id)).ShouldContain(Safe);
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

        Href(await _database.GetAsync(Editor, id)).ShouldBe(Safe);
    }

    [Theory]
    [MemberData(nameof(PlaceNames))]
    public async Task AStoredUnsafeHrefIsLeftOutOfEveryRead(string place)
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

    // The row keeps the link until a write replaces it, and a write that sends the document back as a read gave it does.
    [Fact]
    public async Task AStoredUnsafeHrefStaysUntilAWriteSendsTheReadBack()
    {
        var created = await _database.CreateAsync(Editor, Placed("text", Safe));
        var id = IdOf(created);
        StoreUnsafe();

        ((string)_database.Row("cms_article", id)["body"]).ShouldContain(Script);
        var read = (await _database.GetAsync(Editor, id))!;
        read["title"] = "Again";

        await _database.UpdateAsync(Editor, id, read, VersionOf(created));

        ((string)_database.Row("cms_article", id)["body"]).ShouldNotContain("javascript");
    }

    // A write that carries the stored link on, as a patch of another field or a publish of the draft does, is refused at it.
    [Fact]
    public async Task AWriteThatCarriesAStoredUnsafeHrefIsRefusedAtIt()
    {
        var created = await _database.CreateAsync(Editor, Placed("text", Safe));
        var id = IdOf(created);
        StoreUnsafe();

        var patch = (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, id, new() { ["title"] = "Again" }, VersionOf(created)))).Errors.ShouldHaveSingleItem();
        var publish = (await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(Editor, id))).Errors.ShouldHaveSingleItem();

        patch.Path.ShouldBe("body.content[0].content[0].marks[0].attrs.href");
        publish.Code.ShouldBe(ValidationErrorCodes.UnsafeUrl);
    }

    // Puts javascript: in place of the safe href in every row and version, as data stored before the rule would have it.
    private void StoreUnsafe() => _database.Read(context =>
    {
        foreach (var row in context.Rows("cms_article").ToList())
        {
            foreach (var key in row.Keys.ToList())
            {
                if (row[key] is string text && text.Contains(Safe, StringComparison.Ordinal))
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

    private static string? Href(ContentDocument? entry) =>
        JsonNode.Parse(((RichText)entry!["body"]!).Json)!["content"]![0]!["content"]![0]!["marks"]!.AsArray()
            .Single(mark => (string?)mark!["type"] == "link")!["attrs"]!["href"]!.GetValue<string>();
}
