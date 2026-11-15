using System.Text.Json.Nodes;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Diagnostics;

// Text of an instance, a package or a schema is printed with its control characters and bidi controls escaped, so that an
// escape sequence or a line break in it never reaches the terminal as one.
public sealed class PrintableTextTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string RightToLeftOverride = ((char)0x202E).ToString();
    private static readonly string LeftToRightIsolate = ((char)0x2066).ToString();
    private static readonly string ControlSequenceIntroducer = ((char)0x9B).ToString();

    [Fact]
    public void AControlCharacterOrABidiControlIsEscapedAndOtherTextIsKept()
    {
        PrintableText.Escape($"a{Esc}[2Jb").ShouldBe("a\\u001B[2Jb");
        PrintableText.Escape($"x{RightToLeftOverride}y{LeftToRightIsolate}z").ShouldBe("x\\u202Ey\\u2066z");
        PrintableText.Escape($"{ControlSequenceIntroducer}31m").ShouldBe("\\u009B31m");
        PrintableText.Escape("line\nnext\ttab").ShouldBe("line\\u000Anext\\u0009tab");
        PrintableText.Escape($"line\r\nnext\t{Esc}", keepLineBreaks: true).ShouldBe("line\r\nnext\t\\u001B");
        PrintableText.Escape($"caf{(char)0xE9} {(char)0x4E2D}").ShouldBe($"caf{(char)0xE9} {(char)0x4E2D}");
        PrintableText.Escape(null).ShouldBe("");
    }

    [Fact]
    public void AnErrorOfTheSchemaReaderNamesAPropertyWithItsControlCharactersEscaped()
    {
        var json = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = "post",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = "text" }),
            }),
            [$"extra{RightToLeftOverride}"] = 1,
        }.ToJsonString();

        var exception = Should.Throw<SchemaValidationException>(() => new SchemaBuilder(new UncheckedFieldTypes()).AddSchemaJson(json).Build());

        exception.Errors.ShouldNotBeEmpty();
        exception.Errors.ShouldAllBe(error => !error.Message.Any(PrintableText.IsUnsafe));
        exception.Errors.ShouldContain(error => error.Message.Contains("extra\\u202E", StringComparison.Ordinal));
    }

    [Fact]
    public void AValidatorErrorNamesAnInvalidTypeOrFieldWithItsControlCharactersEscaped()
    {
        var json = new JsonObject
        {
            ["collections"] = new JsonArray(new JsonObject
            {
                ["name"] = $"post{Esc}[2J",
                ["fields"] = new JsonArray(new JsonObject { ["name"] = $"title\n{Esc}]0;x", ["type"] = "text" }),
            }),
        }.ToJsonString();

        var exception = Should.Throw<SchemaValidationException>(() => new SchemaBuilder(new UncheckedFieldTypes()).AddSchemaJson(json).Build());

        exception.Errors.ShouldAllBe(error => !error.Message.Any(PrintableText.IsUnsafe));
        exception.Errors.ShouldContain(error => error.Message.Contains("post\\u001B[2J", StringComparison.Ordinal));
        exception.Errors.ShouldContain(error => error.Message.Contains("title\\u000A\\u001B]0;x", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSectionsAndItemsOfAnImportReportAreEscaped()
    {
        var line = new JsonObject
        {
            ["report"] = new JsonObject
            {
                ["total"] = new JsonObject { ["errors"] = 1 },
                ["sections"] = new JsonObject { [$"content{Esc}[2J"] = new JsonObject { ["errors"] = 1 } },
                ["items"] = new JsonArray(new JsonObject
                {
                    ["outcome"] = "error",
                    ["section"] = "content",
                    ["id"] = "a\nb",
                    ["code"] = "FCMS3201",
                    ["message"] = $"bad{Esc}]0;title",
                }),
            },
        }.ToJsonString();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ImportOutput.WriteReportAsync(line, json: false, output, error);
        var progress = new JsonObject { ["section"] = $"media{Esc}[2J", ["counts"] = new JsonObject() };
        using var document = System.Text.Json.JsonDocument.Parse(progress.ToJsonString());
        await ImportOutput.WriteProgressAsync(document.RootElement, error);

        exitCode.ShouldBe(1);
        (output.ToString() + error).ShouldNotContain(Esc);
        output.ToString().ShouldContain("  content\\u001B[2J: ");
        output.ToString().ShouldContain("error content a\\u000Ab: FCMS3201 bad\\u001B]0;title");
        error.ToString().ShouldContain("media\\u001B[2J: ");
    }
}
