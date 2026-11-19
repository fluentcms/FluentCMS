using System.Text.Json.Nodes;
using FluentCMS.Cli.Generate;
using FluentCMS.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// The text of a snapshot in what generate writes and prints: a control character or a bidi control is escaped, in a
// TypeScript literal and in a refusal of the snapshot.
public sealed class GenerateTextTests
{
    private static readonly char Esc = (char)0x1B;

    [Fact]
    public void ATypeScriptLiteralEscapesControlCharactersAndBidiControls()
    {
        var option = $"draft{(char)0x202E}{(char)0x2066}tfard{(char)0x2069}{(char)0x9B}{Esc}";

        TypeScriptEmitter.Literal(option).ShouldBe("\"draft\\u202e\\u2066tfard\\u2069\\u009b\\u001b\"");
        TypeScriptEmitter.Literal("a\"b\\c\n").ShouldBe("\"a\\\"b\\\\c\\n\"");
    }

    [Fact]
    public void ASelectOptionWithABidiControlIsEscapedInTheUnionOfItsOptions()
    {
        var snapshot = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = "post",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "category", ["type"] = "select", ["options"] = new JsonArray("open", $"closed{(char)0x202E}") }),
            }),
        }.ToJsonString();

        var text = TypeScriptEmitter.Emit(ClientSchema.Read(snapshot, "the test")).Files.Single().Text;

        text.ShouldContain("\"open\" | \"closed\\u202e\"");
        text.ShouldNotContain((char)0x202E);
    }

    [Fact]
    public void TheTypesAMediaFieldAcceptsAreNamedOnlyWhenEveryOneIsATypeOrAFamily()
    {
        JsonObject Media(string name, params string[] accept) => new() { ["name"] = name, ["type"] = "media", ["multiple"] = true, ["accept"] = new JsonArray([.. accept.Select(type => (JsonNode)type)]) };
        var snapshot = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = "post",
                ["fields"] = new JsonArray(Media("files", "image/*", "Application/PDF"), Media("cover", "image/*", "x */ <b>&")),
            }),
        }.ToJsonString();
        var schema = ClientSchema.Read(snapshot, "the test");

        var typeScript = TypeScriptEmitter.Emit(schema).Files.Single().Text;
        var csharp = CSharpEmitter.Emit(schema, "Client").Files.Single(file => file.Name == "Post.g.cs").Text;

        typeScript.ShouldContain("/** media, multiple, accepts image/* or Application/PDF */\n    files?:");
        typeScript.ShouldContain("/** media, multiple */\n    cover?:");
        csharp.ShouldContain("/// <summary>The field <c>files</c> (media, multiple, accepts image/* or Application/PDF).</summary>");
        csharp.ShouldContain("/// <summary>The field <c>cover</c> (media, multiple).</summary>");
        typeScript.ShouldNotContain("<b>");
        csharp.ShouldNotContain("<b>");
    }

    [Fact]
    public void ASnapshotThatIsRefusedIsNamedWithItsControlCharactersEscaped()
    {
        var snapshot = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = $"post{Esc}[2J",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = "text" }),
            }),
        }.ToJsonString();

        var refusal = Should.Throw<CliException>(() => ClientSchema.Read(snapshot, "the test"));

        refusal.Code.ShouldBe("FCMS3370");
        refusal.Message.ShouldContain("post\\u001B[2J");
        refusal.Message.Where(character => character is not ('\r' or '\n')).ShouldAllBe(character => !PrintableText.IsUnsafe(character));
    }

    [Fact]
    public void TheRuleOfAFieldTypeSaysASCII()
    {
        var snapshot = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = "post",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = $"geo{(char)0xE9}" }),
            }),
        }.ToJsonString();

        Should.Throw<CliException>(() => ClientSchema.Read(snapshot, "the test")).Message
            .ShouldContain("a field type starts with an ASCII letter and holds only ASCII letters, digits, '.', '-' and '_'.");
    }
}
