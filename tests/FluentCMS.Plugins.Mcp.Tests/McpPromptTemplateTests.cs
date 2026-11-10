using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Tests;

// The template of a stored prompt: {{name}} and nothing else.
public sealed class McpPromptTemplateTests
{
    [Fact]
    public void TheNamesAreTheArgumentsInTheOrderTheTemplateFirstNamesThem()
    {
        McpPromptTemplate.Names("Use {{tone}} for {{entry}}, and {{tone}} again. Not { {this} }, {{ that }} or {{1st}}.").ShouldBe(["tone", "entry"]);
        McpPromptTemplate.Names("No argument at all.").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Plain text.", false)]
    [InlineData("With {{entry}} and {{_tone2}}.", false)]
    [InlineData("""JSON such as {"a":{"b":1}} closes twice and opens once.""", false)]
    [InlineData("In braces: {{{entry}}}.", false)]
    [InlineData("Spaced: {{ entry }}.", true)]
    [InlineData("An expression: {{entry | upper}}.", true)]
    [InlineData("A section: {{#if entry}}x{{/if}}.", true)]
    [InlineData("Unclosed: {{entry.", true)]
    [InlineData("Joined by what went: {{{entry}}{.", false)]
    public void AnOpeningThatStartsNoArgumentIsStray(string template, bool stray) =>
        McpPromptTemplate.HasStrayOpening(template).ShouldBe(stray);

    [Fact]
    public void FillingPutsEachValueInOnceAndAsItIs()
    {
        Dictionary<string, string> arguments = new() { ["entry"] = "{{tone}}", ["tone"] = "dry", ["unused"] = "x" };

        McpPromptTemplate.Fill("{{entry}} in a {{tone}} tone, {{entry}}{{missing}}.", arguments).ShouldBe("{{tone}} in a dry tone, {{tone}}.");
    }

    [Theory]
    [InlineData("entry", true)]
    [InlineData("_a1", true)]
    [InlineData("1st", false)]
    [InlineData("two words", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AnArgumentNameIsLettersDigitsAndUnderscores(string? name, bool valid) =>
        McpPromptTemplate.IsArgumentName(name).ShouldBe(valid);
}
