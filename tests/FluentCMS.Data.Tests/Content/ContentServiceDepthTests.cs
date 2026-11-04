using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A document as deep as the rich text field type accepts is stored and read back; a deeper one is refused as a validation
// error on its field, and nothing throws a JsonException to the caller.
public sealed class ContentServiceDepthTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    // Nested blockquotes round a paragraph: a JSON depth of 2 * quotes + 3.
    private static string Quoted(int quotes) =>
        """{"type":"doc","content":[""" + string.Concat(Enumerable.Repeat("""{"type":"blockquote","content":[""", quotes))
        + """{"type":"paragraph"}""" + string.Concat(Enumerable.Repeat("]}", quotes)) + "]}";

    private static RichText Body(int quotes) => new(Quoted(quotes));

    private static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 256 });

    private static JsonNode Nested(int depth)
    {
        JsonNode node = new JsonObject();
        for (var level = 1; level < depth; level++)
        {
            node = new JsonObject { ["in"] = node };
        }

        return node;
    }

    private static ContentDocument Titled(object? body) => new() { ["title"] = "T", ["body"] = body };

    private static void ShouldBeRefusedAt(ValidationException refused, string path) =>
        refused.Errors.ShouldContain(error => error.Path == path);

    // 85 levels is the document of the report; 127 is the deepest that is whole with a quote round it.
    [Theory]
    [InlineData(41)]
    [InlineData(49)]
    [InlineData(62)]
    public async Task ADocumentWithinTheLimitIsStoredAndReadBack(int quotes)
    {
        var created = await _database.CreateAsync(Editor, Titled(Body(quotes)));
        var id = IdOf(created);

        var read = await _database.GetAsync(Editor, id);
        JsonNode.DeepEquals(Parse(((RichText)read!["body"]!).Json), Parse(Quoted(quotes))).ShouldBeTrue();

        var updated = await _database.UpdateAsync(Editor, id, Titled(Body(quotes - 1)), VersionOf(created));
        await _database.PatchAsync(Editor, id, new() { ["body"] = Body(quotes) }, VersionOf(updated));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(200)]
    public async Task ADocumentBeyondTheLimitIsRefusedOnCreate(int quotes)
    {
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Titled(Body(quotes))));

        ShouldBeRefusedAt(refused, "body");
    }

    [Theory]
    [InlineData(63)]
    [InlineData(200)]
    public async Task ADocumentBeyondTheLimitIsRefusedOnUpdateAndPatch(int quotes)
    {
        var created = await _database.CreateAsync(Editor, Titled(Body(2)));
        var id = IdOf(created);

        ShouldBeRefusedAt(await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(Editor, id, Titled(Body(quotes)), VersionOf(created))), "body");
        ShouldBeRefusedAt(await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, id, new() { ["body"] = Body(quotes) }, VersionOf(created))), "body");

        var stored = await _database.GetAsync(Editor, id);
        VersionOf(stored!).ShouldBe(VersionOf(created));
    }

    [Fact]
    public async Task ADeepDocumentIsPublishedAndReadAsPublished()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled(Body(62))));

        await _database.PublishAsync(Editor, id);

        var published = await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published });
        JsonNode.DeepEquals(Parse(((RichText)published!["body"]!).Json), Parse(Quoted(62))).ShouldBeTrue();
    }

    [Fact]
    public async Task ADeepDocumentInABlockIsRefusedOnTheBlockList()
    {
        var block = new JsonObject { ["type"] = "hero", ["note"] = Parse(Quoted(62)) };

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "T", ["sections"] = new JsonArray(block) }));

        ShouldBeRefusedAt(refused, "sections");
    }

    [Fact]
    public async Task AJsonValueIsStoredAtTheLimitAndRefusedBeyondIt()
    {
        var created = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["metadata"] = Nested(JsonDepthLimit) });
        var read = await _database.GetAsync(Editor, IdOf(created));
        read!["metadata"].ShouldNotBeNull();

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "T", ["metadata"] = Nested(JsonDepthLimit + 1) }));

        ShouldBeRefusedAt(refused, "metadata");
    }

    private const int JsonDepthLimit = 128;
}
