using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// The data of a block placed inline in rich text follows the rules of the same block in a block list. Each case runs on
// both, with inline false for the block list.
public sealed class InlineBlockRulesTests : IDisposable
{
    private static readonly string[] Cards = ["card", "strict", "gated"];
    private static readonly string[] Kinds = ["ok", "fine"];

    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddComponent("box", component => component.Field("note", "text").Field("internal", "text", field => field.Hidden()))
        .AddBlock("card", block => block
            .Field("label", "text", field => field.Length(null, 10))
            .Field("count", "integer", field => field.Range(0, 5))
            .Field("who", "relation", field => field.Relation("author"))
            .Field("internal", "text", field => field.Hidden())
            .Field("stamp", "text", field => field.IsReadOnly())
            .Field("box", "component", field => field.Setting(FieldSettings.Component, "box")))
        .AddBlock("strict", block => block.Field("title", "text", field => field.Required()))
        .AddBlock("gated", block => block
            .Field("label", "text")
            .Field("kind", "select", field => field.Hidden().Setting(FieldSettings.Options, Kinds))
            .Field("approval", "text", field => field.Hidden().Required()))
        .AddCollection("author", type => type.Field("name", "text"))
        .AddCollection("article", type => type
            .Field("title", "text")
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards))
            .Field("body", "richText"))
        .AddCollection("page", type => type.Field("name", "text").Field("article", "relation", field => field.Relation("article")))
        .Build();

    private readonly ServiceDatabase _database = new(schema: Model());

    public void Dispose() => _database.Dispose();

    public static TheoryData<bool> Both() => new() { false, true };

    private static string Field(bool inline) => inline ? "body" : "cards";

    // Where the data of the first block is, as an error path.
    private static string DataPath(bool inline, int index = 0) => inline ? $"body.content[0].content[{index + 1}].attrs.data" : $"cards[{index}]";

    // The same blocks in a block list or inline in a paragraph, after a text node.
    private static object? Blocks(bool inline, params string[] blocks) => inline
        ? new RichText("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Before"},""" + string.Join(",", blocks.Select(InlineNode)) + "]}]}")
        : JsonNode.Parse("[" + string.Join(",", blocks.Select(ListItem)) + "]");

    // A block as "type:{data}".
    private static string InlineNode(string block)
    {
        var (type, data) = Split(block);
        return $$"""{"type":"inlineBlock","attrs":{"blockType":"{{type}}","data":""" + data + "}}";
    }

    private static string ListItem(string block)
    {
        var (type, data) = Split(block);
        return $$"""{"type":"{{type}}",{{data[1..]}}""";
    }

    private static (string Type, string Data) Split(string block) => (block[..block.IndexOf(':', StringComparison.Ordinal)], block[(block.IndexOf(':', StringComparison.Ordinal) + 1)..]);

    private static ContentDocument Placed(bool inline, params string[] blocks) => new() { ["title"] = "T", [Field(inline)] = Blocks(inline, blocks) };

    private Task<ContentDocument> SystemAsync(Func<IContentService, Task<ContentDocument>> work) =>
        _database.AsAsync(Principal.System, services => work(services.GetRequiredService<IContentService>()), origin: Origin.Seed);

    private async Task<Guid> SystemCreateAsync(bool inline, params string[] blocks) =>
        IdOf(await SystemAsync(service => service.CreateAsync("article", Placed(inline, blocks), Token)));

    private string Stored(Guid id, bool inline) => (string)_database.Row("cms_article", id)[Field(inline)];

    private static string Text(object? value) => JsonSerializer.Serialize(value);

    // A value as a read gave it, with one text changed, in the form a client sends it back.
    private static object? Edited(object? read, string from, string to) => read switch
    {
        RichText document => new RichText(document.Json.Replace(from, to, StringComparison.Ordinal)),
        JsonNode node => JsonNode.Parse(node.ToJsonString().Replace(from, to, StringComparison.Ordinal)),
        _ => throw new InvalidOperationException($"Unexpected {read?.GetType()}"),
    };

    private Principal Writer(params (string Permission, PermissionConstraints? Constraints)[] extra) =>
        WithRole(_database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), [("content.article.read", null), ("content.article.update", null), .. extra]));

    public static TheoryData<bool, string, string, string> Invalid() => new()
    {
        { false, """card:{"label":"far too long"}""", ".label", ValidationErrorCodes.MaxLength },
        { true, """card:{"label":"far too long"}""", ".label", ValidationErrorCodes.MaxLength },
        { false, """card:{"count":9}""", ".count", ValidationErrorCodes.AboveMaximum },
        { true, """card:{"count":9}""", ".count", ValidationErrorCodes.AboveMaximum },
        { false, """card:{"count":"many"}""", ".count", ValidationErrorCodes.InvalidValue },
        { true, """card:{"count":"many"}""", ".count", ValidationErrorCodes.InvalidValue },
        { false, """card:{"who":"not an id"}""", ".who", ValidationErrorCodes.InvalidValue },
        { true, """card:{"who":"not an id"}""", ".who", ValidationErrorCodes.InvalidValue },
        { false, """card:{"box":{"note":5}}""", ".box.note", ValidationErrorCodes.InvalidValue },
        { true, """card:{"box":{"note":5}}""", ".box.note", ValidationErrorCodes.InvalidValue },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task AnInvalidValueIsRefusedOnWrite(bool inline, string block, string at, string code)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Placed(inline, block)))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(DataPath(inline) + at);
        error.Code.ShouldBe(code);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task ARequiredFieldIsCheckedOnPublish(bool inline)
    {
        var id = IdOf(await _database.CreateAsync(Editor, Placed(inline, """strict:{"title":""}""")));

        var error = (await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(Editor, id))).Errors.ShouldHaveSingleItem();

        error.Path.ShouldBe(DataPath(inline) + ".title");
        error.Code.ShouldBe(ValidationErrorCodes.Required);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task AValueIsStoredAsItsFieldTypeReadsIt(bool inline)
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));

        var id = IdOf(await _database.CreateAsync(Editor, Placed(inline, $$"""card:{"who":"{{author}}","count":2}""")));

        var stored = StoredJson.Parse(Stored(id, inline));
        (inline ? stored["content"]![0]!["content"]![1]!["attrs"]!["data"]!["who"] : stored[0]!["who"]).ShouldBeJson($$"""{"id":"{{author}}","type":"author"}""");
        _database.References(id).ShouldHaveSingleItem().TargetId.ShouldBe(author);
    }

    [Fact]
    public async Task AnInlineBlockOfAnUnknownTypeIsLeftAsWritten()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["body"] = new RichText("""{"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"pluginThing","data":{"internal":"anything","count":"many"}}}]}""") }));

        var read = (await _database.GetAsync(Editor, id))!["body"].ShouldBeOfType<RichText>().Json;
        read.ShouldContain("anything");
        read.ShouldContain("many");
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task AHiddenFieldIsAbsentFromEveryRead(bool inline)
    {
        var id = await SystemCreateAsync(inline, """card:{"label":"Shown","internal":"SECRET","box":{"note":"n","internal":"NESTED"}}""");
        await SystemAsync(service => service.PatchAsync("article", id, new() { [Field(inline)] = Blocks(inline, """card:{"label":"Shown","internal":"CHANGED","box":{"note":"n2","internal":"NESTED2"}}""") }, 1, Token));
        await _database.PublishAsync(Editor, id);
        var page = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "p", ["article"] = id }, "page"));
        var reader = WithRole(_database.Database.AddRoleForTest("Readers", ("content.article.read", null), ("content.page.read", null)));

        foreach (var principal in new[] { Editor, reader })
        {
            object?[] reads =
            [
                await _database.GetAsync(principal, id),
                await _database.GetAsync(principal, id, new GetOptions { Status = QueryStatus.Published }),
                await _database.GetAsync(principal, id, new GetOptions { Status = QueryStatus.Published, RichTextFormat = "html" }),
                await _database.GetAsync(principal, id, new GetOptions { Status = QueryStatus.Published, RichTextFormat = "markdown" }),
                (await _database.QueryAsync(principal)).Items,
                await _database.VersionAsync(principal, id, 1),
                await _database.VersionAsync(principal, id, 2),
                await _database.DiffAsync(principal, id, 1, 2),
                await _database.GetAsync(principal, page, new GetOptions { Status = QueryStatus.Draft, Populate = ["article"] }, "page"),
            ];

            foreach (var read in reads)
            {
                var text = Text(read);
                text.ShouldNotContain("SECRET");
                text.ShouldNotContain("CHANGED");
                text.ShouldNotContain("NESTED");
            }

            Text(reads[0]).ShouldContain("Shown");
            Text(reads[2]).ShouldContain(inline ? "data-fcms-data" : "Shown");
            Text(reads[7]).ShouldContain("n2");
            Text(reads[8]).ShouldContain("Shown");
        }
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AnInvalidStoredHiddenValueIsNotQuotedAndDoesNotBlockASave(bool inline, bool isPatch)
    {
        var id = await SystemCreateAsync(inline, """gated:{"label":"A","kind":"ok"}""");
        var column = Field(inline);
        var corrupt = ((string)_database.Row("cms_article", id)[column]).Replace("\"ok\"", "\"LEAKED\"", StringComparison.Ordinal);
        _database.Read(context => context.Rows("cms_article")
            .Where(row => EF.Property<Guid>(row, "document_id") == id)
            .ExecuteUpdate(set => set.SetProperty(row => EF.Property<string>(row, column), corrupt)));

        var read = (await _database.GetAsync(Editor, id))!;
        Text(read).ShouldNotContain("LEAKED");
        read[column] = Edited(read[column], "\"A\"", "\"B\"");
        if (isPatch)
        {
            await _database.PatchAsync(Editor, id, new() { [column] = read[column] }, 1);
        }
        else
        {
            await _database.UpdateAsync(Editor, id, read, 1);
        }

        Stored(id, inline).ShouldContain("\"B\"");
        Stored(id, inline).ShouldContain("LEAKED");
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task APublishErrorOnAHiddenFieldDoesNotNameIt(bool inline)
    {
        var id = await SystemCreateAsync(inline, """gated:{"label":"A"}""");

        var errors = (await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(Editor, id))).Errors;

        errors.ShouldHaveSingleItem().ShouldBe(Core.Content.EntryErrors.UnreadableValue);
        Text(errors).ShouldNotContain("approval");
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task AFilterCannotReachAHiddenField(bool inline)
    {
        await SystemCreateAsync(inline, """card:{"label":"Shown","internal":"SECRET"}""");
        var path = inline ? "body.card.internal" : "cards.card.internal";

        await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Filter = new FieldFilter(path, FilterOperator.Equal, "SECRET") }));
    }

    public static TheoryData<bool, bool> Writes() => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AReadThenAWriteKeepsHiddenValues(bool inline, bool isPatch)
    {
        var id = await SystemCreateAsync(inline, """card:{"label":"A","internal":"SECRET","box":{"note":"n","internal":"NESTED"}}""");

        var read = (await _database.GetAsync(Editor, id))!;
        read[Field(inline)] = Edited(read[Field(inline)], "\"A\"", "\"B\"");
        if (isPatch)
        {
            await _database.PatchAsync(Editor, id, new() { [Field(inline)] = read[Field(inline)] }, 1);
        }
        else
        {
            await _database.UpdateAsync(Editor, id, read, 1);
        }

        var stored = Stored(id, inline);
        stored.ShouldContain("\"B\"");
        stored.ShouldNotContain("\"A\"");
        stored.ShouldContain("SECRET");
        stored.ShouldContain("NESTED");
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task HiddenValuesFollowBlocksThatMove(bool inline)
    {
        var id = await SystemCreateAsync(inline, """card:{"label":"A","internal":"A-approved"}""", """card:{"label":"B","internal":"B-rejected"}""");

        await _database.PatchAsync(Editor, id, new() { [Field(inline)] = Blocks(inline, """card:{"label":"B"}""", """card:{"label":"A"}""", """card:{"label":"C"}""") }, 1);

        var stored = Stored(id, inline);
        stored.IndexOf("B-rejected", StringComparison.Ordinal).ShouldBeLessThan(stored.IndexOf("A-approved", StringComparison.Ordinal));
        stored.IndexOf("\"B\"", StringComparison.Ordinal).ShouldBeLessThan(stored.IndexOf("B-rejected", StringComparison.Ordinal));
        stored.IndexOf("A-approved", StringComparison.Ordinal).ShouldBeLessThan(stored.IndexOf("\"C\"", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task TheSystemPrincipalMayChangeAHiddenValue(bool inline)
    {
        var id = await SystemCreateAsync(inline, """card:{"label":"A","internal":"old"}""");

        await SystemAsync(service => service.PatchAsync("article", id, new() { [Field(inline)] = Blocks(inline, """card:{"label":"A","internal":"new"}""") }, 1, Token));

        Stored(id, inline).ShouldContain("\"new\"");
        Stored(id, inline).ShouldNotContain("\"old\"");
    }

    public static TheoryData<bool, string, string> Unwritable() => new()
    {
        { false, """card:{"label":"A","internal":"x"}""", "cards.internal" },
        { true, """card:{"label":"A","internal":"x"}""", "body.internal" },
        { false, """card:{"label":"A","stamp":"x"}""", "cards.stamp" },
        { true, """card:{"label":"A","stamp":"x"}""", "body.stamp" },
        { false, """card:{"label":"A","box":{"internal":"x"}}""", "cards.box.internal" },
        { true, """card:{"label":"A","box":{"internal":"x"}}""", "body.box.internal" },
        { false, """card:{"label":"A","nope":"x"}""", "cards.nope" },
        { true, """card:{"label":"A","nope":"x"}""", "body.nope" },
    };

    [Theory]
    [MemberData(nameof(Unwritable))]
    public async Task AWriterCannotSetAHiddenReadOnlyOrUnknownField(bool inline, string block, string path)
    {
        var id = await SystemCreateAsync(inline, """card:{"label":"A","internal":"SECRET","stamp":"S"}""");

        foreach (var write in new Func<Task>[]
        {
            () => _database.CreateAsync(Editor, Placed(inline, block)),
            () => _database.PatchAsync(Editor, id, new() { [Field(inline)] = Blocks(inline, block) }, 1),
            () => _database.UpdateAsync(Editor, id, Placed(inline, block), 1),
        })
        {
            var error = (await Should.ThrowAsync<ValidationException>(write)).Errors.ShouldHaveSingleItem();
            error.Path.ShouldBe(path);
            error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
        }

        Stored(id, inline).ShouldContain("SECRET");
    }

    [Fact]
    public async Task AHiddenFieldCannotBeSetThroughRichTextInAnyFormTheFieldTakes()
    {
        const string Json = """{"type":"doc","content":[{"type":"inlineBlock","attrs":{"blockType":"card","data":{"internal":"x"}}}]}""";
        foreach (var body in new object[] { Json, JsonNode.Parse(Json)!, JsonValue.Create(Json), JsonSerializer.SerializeToElement(JsonNode.Parse(Json)), new RichText(Json) })
        {
            var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["body"] = body }))).Errors.ShouldHaveSingleItem();
            error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
        }
    }

    [Theory]
    [MemberData(nameof(Both))]
    public async Task AnUnreadableTargetIsNoTargetOnReadAndKeptOnWrite(bool inline)
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        await _database.PublishAsync(Editor, author, type: "author");
        var id = IdOf(await _database.CreateAsync(Editor, Placed(inline, $$"""card:{"label":"A","who":"{{author}}"}""")));
        await _database.PublishAsync(Editor, id);
        var writer = Writer();

        var read = (await _database.GetAsync(writer, id))!;
        Text(read).ShouldNotContain(author.ToString());
        Text(await _database.GetAsync(writer, id, new GetOptions { Status = QueryStatus.Published })).ShouldNotContain(author.ToString());
        Text(await _database.VersionAsync(writer, id, 1)).ShouldNotContain(author.ToString());

        read[Field(inline)] = Edited(read[Field(inline)], "\"A\"", "\"B\"");
        await _database.UpdateAsync(writer, id, read, 1);

        Stored(id, inline).ShouldContain("\"B\"");
        _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(author);
    }
}
