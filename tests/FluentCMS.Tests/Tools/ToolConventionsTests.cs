using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;

namespace FluentCMS.Tests.Tools;

public sealed class ToolConventionsTests
{
    [Fact]
    public void The_reader_returns_the_arguments_it_is_asked_for()
    {
        var reader = Reader("""{"slug": "product", "version": 2, "id": "0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e"}""");

        Assert.Equal("product", reader.RequiredString("slug", "product"));
        Assert.Equal(2, reader.OptionalPositiveInteger("version"));
        Assert.Equal(Guid.Parse("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e"), reader.RequiredGuid("id"));
        Assert.Null(reader.OptionalString("summary", "short"));
        Assert.Empty(reader.Errors());
    }

    [Fact]
    public void A_missing_malformed_or_unknown_argument_is_reported_by_name()
    {
        var reader = Reader("""{"version": 0, "id": "not-a-uuid", "slg": "product", "summary": "  "}""");

        reader.RequiredString("slug", "product");
        reader.OptionalPositiveInteger("version");
        reader.RequiredGuid("id");
        reader.OptionalString("summary", "short");
        var errors = reader.Errors();

        Assert.Equal(
            [
                (ToolErrorCodes.ArgumentMissing, "'slug' is required: give a string such as 'product'."),
                (ToolErrorCodes.ArgumentInvalid, "'version' must be a whole number greater than zero."),
                (ToolErrorCodes.ArgumentInvalid, "'id' must be a UUID string such as '0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e'."),
                (ToolErrorCodes.ArgumentInvalid, "'summary' must be a non-empty string such as 'short'."),
                (ToolErrorCodes.ArgumentUnknown, "'slg' is not an argument of this tool; it takes 'slug', 'version', 'id', 'summary'."),
            ],
            errors.Select(error => (error.Code, error.Message)));
    }

    [Fact]
    public void A_tool_without_arguments_accepts_none_and_null_and_refuses_a_value_that_is_not_an_object()
    {
        Assert.Empty(Reader("{}").Errors());
        Assert.Empty(new ToolArgumentsReader(default).Errors());
        Assert.Empty(Reader("null").Errors());
        Assert.Equal("This tool takes no arguments, and 'slug' was given.", Assert.Single(Reader("""{"slug": "product"}""").Errors()).Message);
        Assert.Equal(ToolErrorCodes.ArgumentsNotObject, Assert.Single(Reader("[1]").Errors()).Code);
    }

    [Fact]
    public void A_result_is_the_value_or_the_errors_with_a_hint()
    {
        var success = ToolResult.Success(new JsonObject { ["slug"] = "product" });
        var failure = ToolResult.From(
            Result<JsonObject>.Failure("content_type_not_found", "There is no content type 'produkt' in this site."),
            value => value,
            "Call list_content_types for the slugs.");

        Assert.Equal("""{"slug":"product"}""", success.ToJson()!.ToJsonString());
        Assert.False(failure.IsSuccess);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"errors":[{"code":"content_type_not_found","message":"There is no content type 'produkt' in this site."}],"hint":"Call list_content_types for the slugs."}"""),
            failure.ToJson()));
        Assert.Throws<ArgumentException>(() => ToolResult.Failure([]));
    }

    [Fact]
    public void A_resource_template_matches_one_segment_per_parameter_and_unescapes_it()
    {
        var resource = new AgentResource(
            "fluentcms://schema/{slug}",
            "content_type_schema",
            "The schema of one content type, for the tests.",
            "application/json",
            (_, _) => Task.FromResult(Result<string>.Success("{}")));

        Assert.True(resource.IsTemplated);
        Assert.Equal("product-page", resource.Match("fluentcms://schema/product-page")!["slug"]);
        Assert.Equal("a b", resource.Match("fluentcms://schema/a%20b")!["slug"]);
        Assert.Null(resource.Match("fluentcms://schema/product/fields"));
        Assert.Null(resource.Match("fluentcms://schema/"));
        Assert.Null(resource.Match("fluentcms://schemas/product"));
    }

    [Theory]
    [InlineData("get_content_type", true)]
    [InlineData("list", true)]
    [InlineData("get2_items", true)]
    [InlineData("Get_content_type", false)]
    [InlineData("get-content-type", false)]
    [InlineData("get__type", false)]
    [InlineData("_get", false)]
    [InlineData("", false)]
    public void Tool_names_are_lowercase_words_joined_by_underscores(string name, bool valid)
    {
        Assert.Equal(valid, ToolNames.IsName(name));
    }

    private static ToolArgumentsReader Reader(string json)
    {
        return new ToolArgumentsReader(JsonDocument.Parse(json).RootElement);
    }
}
