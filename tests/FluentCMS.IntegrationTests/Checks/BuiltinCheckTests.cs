using FluentCMS.Core.Checks;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Schema;
using static FluentCMS.IntegrationTests.Checks.CheckScenario;

namespace FluentCMS.IntegrationTests.Checks;

// The built-in check types reach the directory as plugins would (EXT-N1), and their examples gate activation like any other.
public sealed class BuiltinCheckTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Checks_of_the_reference_scenario_activate_once_their_examples_pass(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateContentTypeAsync(
            "product-page",
            SchemaScenario.Field("headline", "text", """{"maxLength": 80}"""),
            SchemaScenario.Field("body", "richText"),
            SchemaScenario.Field("price", "number"));

        var checks = new Dictionary<string, CheckDefinition>
        {
            ["no-superlatives"] = Definition("terms", """{"terms": ["best in class"]}""", """{"headline": "A sturdy kettle"}""", """{"headline": "The best-in-class kettle"}"""),
            ["alt-text"] = Definition("altText", "{}", """{"body": "![A steel kettle](/media/k.jpg)"}""", """{"body": "![](/media/k.jpg)"}"""),
            ["sourced-prices"] = Definition("sources", "{}", """{"headline": "No price here"}""", """{"price": 199}""", ["price"]),
            ["page-schema"] = Definition("schema", "{}", """{"headline": "A kettle"}""", """{"headline": 5}"""),
        };

        foreach (var (name, definition) in checks)
        {
            Assert.True((await scenario.CreateAsync(name, definition)).IsSuccess, name);
            var activated = await scenario.RunAsync(directory => directory.ActivateAsync(name, 1));
            Assert.True(activated.IsSuccess, $"{name}: {string.Join("; ", activated.Errors.Select(error => error.Message))}");
        }

        Assert.Equal(
            ["alt-text", "no-superlatives", "page-schema", "sourced-prices"],
            (await scenario.RunAsync(directory => directory.ListActiveAsync())).Select(check => check.Name));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_built_in_check_with_an_example_it_gets_wrong_is_not_activated(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateContentTypeAsync("product-page", SchemaScenario.Field("headline", "text"));
        await scenario.CreateAsync(
            "no-superlatives",
            Definition("terms", """{"terms": ["best in class"]}""", """{"headline": "Best in class"}""", """{"headline": "The best kettle"}"""));

        var refused = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));

        Assert.Equal(
            [
                "Example 'passes' should pass, but the check failed it: "
                    + "Remove or rewrite 'Best in class' in headline; 'best in class' is on this check's list of terms not to use.",
                "Example 'fails' should fail, but the check passed it.",
            ],
            refused.Errors.Select(error => error.Message));
    }

    private static CheckDefinition Definition(string type, string settings, string passing, string failing, IReadOnlyList<string>? fields = null)
    {
        return new CheckDefinition(
            type,
            $"The {type} check of the reference scenario.",
            Parse(settings),
            [Example("passes", CheckVerdict.Pass, passing, "product-page"), Example("fails", CheckVerdict.Fail, failing, "product-page")],
            scope: new CheckScope(["product-page"], fields));
    }
}
