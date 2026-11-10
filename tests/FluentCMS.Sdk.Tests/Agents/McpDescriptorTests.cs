using System.Text.Json;
using FluentCMS.Agents;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Agents;

public class McpDescriptorTests
{
    private static readonly JsonElement ObjectSchema = Json("""{ "type": "object" }""");

    [Theory]
    [InlineData("article_list")]
    [InlineData("media.get-2")]
    [InlineData("A")]
    public void AToolTakesANameOfLettersDigitsUnderscoresHyphensAndDots(string name)
    {
        McpToolDescriptor.IsValidName(name).ShouldBeTrue();
        new McpToolDescriptor(name, "Does something.", ObjectSchema).Name.ShouldBe(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("article/list")]
    [InlineData("liste_café")]
    [InlineData("article_list\n")]
    public void AToolRejectsAnyOtherName(string? name)
    {
        McpToolDescriptor.IsValidName(name).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => new McpToolDescriptor(name!, "Does something.", ObjectSchema));
    }

    [Fact]
    public void AToolNameIsAtMost128Characters()
    {
        McpToolDescriptor.IsValidName(new string('a', 128)).ShouldBeTrue();
        McpToolDescriptor.IsValidName(new string('a', 129)).ShouldBeFalse();
    }

    [Fact]
    public void AToolNeedsADescriptionAndAnObjectSchema()
    {
        Should.Throw<ArgumentException>(() => new McpToolDescriptor("article_list", " ", ObjectSchema));
        Should.Throw<ArgumentException>(() => new McpToolDescriptor("article_list", "Lists articles.", Json("[]")));
        Should.Throw<ArgumentException>(() => new McpToolDescriptor("article_list", "Lists articles.", default));
    }

    // The server adds to a description, so a descriptor is copied with another one.
    [Fact]
    public void AToolIsCopiedWithALongerDescription()
    {
        var tool = new McpToolDescriptor("article_list", "Lists articles.", ObjectSchema) { Title = "Articles" };

        var extended = tool with { Description = tool.Description + " Write in British English." };

        extended.Description.ShouldBe("Lists articles. Write in British English.");
        (extended.Name, extended.Title).ShouldBe(("article_list", "Articles"));
        tool.Description.ShouldBe("Lists articles.");
    }

    [Fact]
    public void AToolThatSaysNothingOfItselfIsTakenAsOneThatMayDestroy()
    {
        var hints = new McpToolDescriptor("article_delete", "Deletes an article.", ObjectSchema).Annotations;

        (hints.ReadOnly, hints.Destructive, hints.Idempotent, hints.OpenWorld).ShouldBe((false, true, false, false));
    }

    [Fact]
    public void AToolResultIsTextStructuredContentOrAFailureWithItsCode()
    {
        var text = McpToolResult.FromText("done");
        var structured = McpToolResult.FromJson(Json("""{ "id": 7 }"""));
        var failed = McpToolResult.Error("FCMS3104", "No such entry.");

        (text.Text, text.IsError, text.ErrorCode, text.StructuredContent).ShouldBe(("done", false, null, null));
        structured.Text.ShouldBe("""{ "id": 7 }""");
        structured.StructuredContent.ShouldNotBeNull().GetProperty("id").GetInt32().ShouldBe(7);
        (failed.Text, failed.IsError, failed.ErrorCode).ShouldBe(("No such entry.", true, "FCMS3104"));
        Should.Throw<ArgumentException>(() => McpToolResult.FromJson(Json("[1]")));
        Should.Throw<ArgumentException>(() => McpToolResult.Error("", "No such entry."));
        Should.Throw<ArgumentException>(() => McpToolResult.Error("FCMS3104", ""));
    }

    [Theory]
    [InlineData("fluentcms://schema", false)]
    [InlineData("fluentcms://schema/{type}", true)]
    [InlineData("fluentcms://sites/{site}/guidelines", true)]
    public void AResourceWithAnExpressionInItsUriIsATemplate(string uri, bool template)
    {
        var resource = new McpResourceDescriptor(uri, "schema");

        resource.IsTemplate.ShouldBe(template);
        Should.Throw<ArgumentException>(() => new McpResourceDescriptor(" ", "schema"));
        Should.Throw<ArgumentException>(() => new McpResourceDescriptor(uri, ""));
    }

    [Fact]
    public void ResourceContentsAreTextOrBytes()
    {
        var text = McpResourceContent.FromText("fluentcms://schema", "{}", "application/json");
        var bytes = McpResourceContent.FromBytes("fluentcms://logo", new byte[] { 1, 2, 3 });

        (text.Uri, text.Text, text.MimeType, text.Data.IsEmpty).ShouldBe(("fluentcms://schema", "{}", "application/json", true));
        (bytes.Text, bytes.MimeType, bytes.Data.Length).ShouldBe((null, "application/octet-stream", 3));
        McpResourceContent.FromText("fluentcms://note", "plain").MimeType.ShouldBe("text/plain");
    }

    [Fact]
    public void APromptHasANameAndArguments()
    {
        var prompt = new McpPromptDescriptor("review") { Arguments = [new McpPromptArgument("entry") { Required = true }, new McpPromptArgument("tone")] };

        prompt.Arguments.Select(argument => (argument.Name, argument.Required)).ShouldBe([("entry", true), ("tone", false)]);
        Should.Throw<ArgumentException>(() => new McpPromptDescriptor(" "));
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
