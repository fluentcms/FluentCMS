using System.Text.Json.Nodes;
using FluentCMS.Core.ProseMirror;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.ProseMirror;

// The round trip set of the admin's rich text editor (admin spec 4.7 and 7), which its schema test in
// admin/src/FluentCMS.Admin.RichText/js reads too: each document the editor writes is taken by the server and read back
// unchanged.
public class EditorRoundTripTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "ProseMirror", "EditorDocuments");

    public static TheoryData<string> Documents() => [.. Directory.EnumerateFiles(Folder, "*.json").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheSetIsCopied() => Documents().Count.ShouldBe(4);

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheServerTakesTheDocumentAndReadsItBackUnchanged(string name)
    {
        var json = File.ReadAllText(Path.Combine(Folder, name));

        RichTextParser.TryParse(json, out var document, out var errors).ShouldBeTrue(string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}")));
        var read = RichTextSerializer.Serialize(document);

        JsonNode.DeepEquals(JsonNode.Parse(read), JsonNode.Parse(json)).ShouldBeTrue(read);
    }
}
