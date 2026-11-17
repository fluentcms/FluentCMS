using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// The rules the form checks itself, as the server's declarative validator does: lengths of a text in characters and of a
// list in items, range of numbers and pattern of texts, each item of a multiple field at its own path.
public sealed class FieldRulesTests : AdminTestContext
{
    public FieldRulesTests()
    {
        UseNeutralResources();
        Start();
    }

    private IStringLocalizer<AdminResources> L => Services.GetRequiredService<IStringLocalizer<AdminResources>>();

    [Fact]
    public void ATextIsLimitedInCharacters()
    {
        var field = Field("title", new() { MinLength = 3, MaxLength = 5 });

        Codes(field, "ab").ShouldBe(["minLength"]);
        Codes(field, "abcdef").ShouldBe(["maxLength"]);
        Codes(field, "abcd").ShouldBeEmpty();
        FieldRules.Check(field, "abcdef", L).Single().Message.ShouldBe("Enter at most 5 characters. This has 6.");
    }

    [Fact]
    public void AListIsLimitedInItemsAndEachItemIsCheckedAtItsPath()
    {
        var field = Field("tags", new() { MaxLength = 2, Pattern = "^[a-z]+$" }, multiple: true);

        var errors = FieldRules.Check(field, new JsonArray("news", "Tech", "web"), L);

        errors.Select(error => (error.Path, error.Code)).ShouldBe([("tags", "maxLength"), ("tags[1]", "patternMismatch")]);
        errors[0].Message.ShouldBe("Keep at most 2 items. This has 3.");
    }

    [Fact]
    public void ANumberIsCheckedAgainstItsRange()
    {
        var field = Field("rating", new() { Minimum = 1, Maximum = 5 }, type: "integer");

        Codes(field, 0).ShouldBe(["belowMinimum"]);
        Codes(field, 6).ShouldBe(["aboveMaximum"]);
        Codes(field, 5).ShouldBeEmpty();
        Codes(field, JsonNode.Parse("1e400")).ShouldBe(["aboveMaximum"]);
        FieldRules.Check(field, 6, L).Single().Message.ShouldBe("Enter 5 or less.");
    }

    [Fact]
    public void ATextIsCheckedAgainstItsPattern()
    {
        var field = Field("slug", new() { Pattern = "^[a-z0-9-]+$" });

        Codes(field, "hello-world").ShouldBeEmpty();
        Codes(field, "Hello World").ShouldBe(["patternMismatch"]);
    }

    [Fact]
    public void NoValueRequiredAndUniqueAreLeftOutAndAPatternThatDoesNotReadIsTheServers()
    {
        Codes(Field("title", new() { MinLength = 3, Required = true, Unique = true }), null).ShouldBeEmpty();
        Codes(Field("title", new() { MinLength = 3 }), "  ").ShouldBeEmpty();
        Codes(Field("title", new() { Pattern = "([a-z" }), "anything").ShouldBeEmpty();
        FieldRules.Required(Field("title", new()), L).Message.ShouldBe("A value is required to publish.");
    }

    private string?[] Codes(SchemaFieldDescription field, JsonNode? value) => [.. FieldRules.Check(field, value, L).Select(error => error.Code)];

    private static SchemaFieldDescription Field(string name, FieldValidationDescription rules, string type = "text", bool multiple = false) =>
        new() { Name = name, Type = type, Multiple = multiple, Validation = rules };
}
