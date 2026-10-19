using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

public sealed class CheckDefinitionTests
{
    private static readonly CheckTypeRegistry CheckTypes = new([new MaxWordsCheckType(), new DefinedFieldsCheckType<object>()]);

    private static readonly IServiceProvider NoServices = new ServiceCollection().BuildServiceProvider();

    private const string ShortTitles = """
        {
          "type": "sample.maxWords",
          "description": "Titles are at most three words.",
          "severity": "warn",
          "settings": {"max": 3},
          "scope": {"contentTypes": ["product-page", "blog-post"], "fields": ["title"]},
          "examples": [
            {"name": "a short title", "expect": "pass", "contentType": "blog-post", "fields": {"title": "Three short words"}},
            {"name": "a long title", "expect": "fail", "contentType": "product-page", "fields": {"title": "Four words is too many"}, "sources": ["style guide"]}
          ]
        }
        """;

    [Fact]
    public void A_stored_document_reads_back_as_written()
    {
        var definition = CheckDefinition.Parse(ShortTitles).Value;
        var reread = CheckDefinition.Parse(definition.ToJson()).Value;

        foreach (var read in new[] { definition, reread })
        {
            Assert.Equal("sample.maxWords", read.Type);
            Assert.Equal("Titles are at most three words.", read.Description);
            Assert.Equal(CheckSeverity.Warn, read.Severity);
            Assert.Equal("""{"max":3}""", Compact(read.Settings));
            Assert.Equal(["product-page", "blog-post"], read.Scope.ContentTypes!);
            Assert.Equal(["title"], read.Scope.Fields!);
            Assert.Collection(
                read.Examples,
                example =>
                {
                    Assert.Equal(("a short title", CheckVerdict.Pass, "blog-post"), (example.Name, example.Expected, example.ContentType));
                    Assert.Equal("""{"title":"Three short words"}""", Compact(example.Fields));
                    Assert.Empty(example.Sources);
                },
                example =>
                {
                    Assert.Equal(("a long title", CheckVerdict.Fail, "product-page"), (example.Name, example.Expected, example.ContentType));
                    Assert.Equal(["style guide"], example.Sources);
                });
        }

        Assert.Equal(definition.ToJson(), reread.ToJson());
        Assert.True(definition.Validate(CheckTypes).IsSuccess);
    }

    [Fact]
    public void Severity_settings_and_scope_may_be_left_out()
    {
        var definition = CheckDefinition.Parse("""
            {"type": "sample.maxWords", "description": "Short.", "examples": [{"name": "a", "expect": "pass", "fields": {}}], "scope": null}
            """).Value;

        Assert.Equal(CheckSeverity.Block, definition.Severity);
        Assert.Equal("{}", Compact(definition.Settings));
        Assert.Same(CheckScope.All, definition.Scope);
        Assert.DoesNotContain("scope", definition.ToJson());
    }

    [Fact]
    public void A_malformed_document_reports_every_problem_with_its_path()
    {
        var parsed = CheckDefinition.Parse("""
            {
              "type": 5,
              "severity": "error",
              "settings": [],
              "scope": {"contentTypes": "product-page", "types": []},
              "examples": [
                {"name": "a", "expect": "maybe", "fields": "title", "extra": 1},
                {"expect": "pass", "fields": {}, "sources": [1]},
                3
              ],
              "author": "someone"
            }
            """);

        Assert.All(parsed.Errors, error => Assert.Equal(CheckErrorCodes.DefinitionMalformed, error.Code));
        Assert.Equal(
            [
                "$.type must be a string.",
                "$.severity must be \"block\" or \"warn\".",
                "$.settings must be an object.",
                "$.scope.contentTypes must be an array of strings.",
                "$.scope.types is not part of a scope; it has 'contentTypes' and 'fields'.",
                "$.examples[0].expect must be \"pass\" or \"fail\".",
                "$.examples[0].fields must be an object with one property per field.",
                "$.examples[0].extra is not part of an example; it has 'name', 'expect', 'contentType', 'fields' and 'sources'.",
                "$.examples[1].sources must be an array of strings.",
                "$.examples[1].name is missing.",
                "$.examples[2] must be an object.",
                "$.author is not part of a check; it has 'type', 'description', 'severity', 'settings', 'scope' and 'examples'.",
                "$.description is missing.",
            ],
            parsed.Errors.Select(error => error.Message));
        Assert.Equal(CheckErrorCodes.DefinitionMalformed, Assert.Single(CheckDefinition.Parse("{").Errors).Code);
        Assert.Equal(CheckErrorCodes.DefinitionMalformed, Assert.Single(CheckDefinition.Parse("""{"type": "a", "type": "b"}""").Errors).Code);
    }

    [Fact]
    public void Validation_reports_every_problem_at_once_naming_each_example()
    {
        var definition = new CheckDefinition(
            "sample.maxWords",
            " ",
            Parse("""{"max": 0, "limit": 3}"""),
            [
                Example("a long title", CheckVerdict.Pass, """{"title": "x"}"""),
                Example("A Long Title", CheckVerdict.Pass, """{"title": "x"}""", "product-page"),
                Example("", CheckVerdict.NeedsReview, "[]", "Blog Post"),
                new CheckExample("sourced", CheckVerdict.Pass, Parse("{}"), [" "]),
            ],
            (CheckSeverity)9,
            new CheckScope(["blog-post", "Blog", "blog-post"], []));

        var errors = definition.Validate(CheckTypes).Errors;

        Assert.Equal(
            [
                (SchemaErrorCodes.SettingInvalid, "The setting 'max' must be a whole number greater than zero."),
                (SchemaErrorCodes.SettingUnknown, "The 'sample.maxWords' check type has no setting 'limit'; its settings are max."),
                (CheckErrorCodes.DescriptionInvalid, "The description must say what the check enforces, in at most 1000 characters."),
                (CheckErrorCodes.SeverityInvalid, "The severity '9' is not one of block or warn."),
                (CheckErrorCodes.ScopeInvalid, "The scope's contentTypes hold 'Blog', which is not a content type slug such as 'product-page'."),
                (CheckErrorCodes.ScopeInvalid, "The scope's contentTypes hold 'blog-post' more than once."),
                (CheckErrorCodes.ScopeInvalid, "The scope's fields must not be empty; leave it out to apply to all of them."),
                (CheckErrorCodes.ExamplesMissing,
                    "A check needs at least one example it must pass and one it must fail, so its examples show what it accepts and what it refuses."),
                (CheckErrorCodes.ExampleInvalid,
                    "Example 'a long title' names no content type, but the check applies only to blog-post, Blog; name one of them."),
                (CheckErrorCodes.ExampleInvalid, "Example 'A Long Title' has the same name as another example; names are compared ignoring case."),
                (CheckErrorCodes.ExampleInvalid,
                    "Example 'A Long Title' is of the content type 'product-page', which the check does not apply to; use one of blog-post, Blog."),
                (CheckErrorCodes.ExampleInvalid, "Example 3 needs a name of at most 200 characters that says what it shows."),
                (CheckErrorCodes.ExampleInvalid, "Example 3 must expect the check to pass or fail."),
                (CheckErrorCodes.ExampleInvalid, "Example 3 must give its fields as a JSON object with one property per field."),
                (CheckErrorCodes.ExampleInvalid, "Example 3 names the content type 'Blog Post', which is not a slug such as 'product-page'."),
                (CheckErrorCodes.ExampleInvalid, "Example 'sourced' has a blank source."),
                (CheckErrorCodes.ExampleInvalid,
                    "Example 'sourced' names no content type, but the check applies only to blog-post, Blog; name one of them."),
            ],
            errors.Select(error => (error.Code, error.Message)));
    }

    // What Validate accepts is stored and read back as it is, so a stored check never becomes unreadable.
    [Fact]
    public void A_definition_that_could_not_be_read_back_is_refused_instead_of_stored()
    {
        var duplicateSetting = Parse("""{"max": 3, "max": 4}""");
        var examples = new[] { Example("short", CheckVerdict.Pass, "{}"), Example("long", CheckVerdict.Fail, "{}") };
        var noFields = new CheckExample("no fields", CheckVerdict.Fail, default);

        var duplicate = new CheckDefinition("sample.maxWords", "Short.", duplicateSetting, examples).Validate(CheckTypes);
        var undefined = new CheckDefinition("sample.maxWords", "Short.", Parse("""{"max": 3}"""), [examples[0], noFields]).Validate(CheckTypes);

        Assert.Equal(CheckErrorCodes.DefinitionMalformed, Assert.Single(duplicate.Errors).Code);
        Assert.Equal("Example 'no fields' must give its fields as a JSON object with one property per field.", Assert.Single(undefined.Errors).Message);
        Assert.Equal(CheckErrorCodes.DefinitionMalformed, Assert.Single(CheckDefinition.Parse(duplicateSetting).Errors).Code);
        Assert.Equal(CheckErrorCodes.DefinitionMalformed, Assert.Single(CheckDefinition.Parse(default(JsonElement)).Errors).Code);
        Assert.Equal(
            "The definition holds text that is not valid Unicode.",
            Assert.Single(CheckDefinition.Parse("""{"type": "\ud800", "description": "x", "examples": []}""").Errors).Message);
    }

    [Fact]
    public void An_unknown_type_is_reported_and_a_check_has_at_most_fifty_examples()
    {
        var examples = Enumerable.Range(1, 51)
            .Select(index => Example($"example {index}", index % 2 == 0 ? CheckVerdict.Pass : CheckVerdict.Fail, "{}"));

        var errors = new CheckDefinition("sample.gone", "Gone.", null, examples).Validate(CheckTypes).Errors;

        Assert.Equal([CheckErrorCodes.CheckTypeUnknown, CheckErrorCodes.ExampleInvalid], errors.Select(error => error.Code));
        Assert.Equal("A check has at most 50 examples, and this one has 51.", errors[1].Message);
    }

    [Fact]
    public async Task Examples_that_get_their_verdict_pass_the_gate()
    {
        var definition = CheckDefinition.Parse(ShortTitles).Value;

        var result = await definition.RunExamplesAsync(CheckTypes, new Dictionary<string, ContentTypeDefinition>(), NoServices);

        Assert.True(result.IsSuccess);
    }

    // CHK-02: the failing example is named, with what the check said about it.
    [Fact]
    public async Task Every_example_that_does_not_get_its_verdict_is_named_with_what_happened()
    {
        var definition = new CheckDefinition(
            "sample.maxWords",
            "Titles are at most two words.",
            Parse("""{"max": 2}"""),
            [
                Example("a short title", CheckVerdict.Pass, """{"title": "Short title"}"""),
                Example("looks short", CheckVerdict.Pass, """{"title": "Not short at all", "subtitle": "Nor is this one"}"""),
                Example("looks long", CheckVerdict.Fail, """{"title": "Short"}"""),
            ]);

        var errors = (await definition.RunExamplesAsync(CheckTypes, new Dictionary<string, ContentTypeDefinition>(), NoServices)).Errors;

        Assert.All(errors, error => Assert.Equal(CheckErrorCodes.ExampleFailed, error.Code));
        Assert.Equal(
            [
                "Example 'looks short' should pass, but the check failed it: Shorten this to at most 2 words; it has 4. (and 1 more)",
                "Example 'looks long' should fail, but the check passed it.",
            ],
            errors.Select(error => error.Message));
    }

    [Fact]
    public async Task An_example_the_type_cannot_decide_does_not_pass_the_gate()
    {
        var definition = new CheckDefinition(
            "sample.gone",
            "Gone.",
            null,
            [Example("anything", CheckVerdict.Pass, "{}"), Example("nothing", CheckVerdict.Fail, "{}")]);

        var errors = (await definition.RunExamplesAsync(CheckTypes, new Dictionary<string, ContentTypeDefinition>(), NoServices)).Errors;

        Assert.Equal(
            "Example 'anything' could not be decided: The check type 'sample.gone' is not installed, so the check could not run.",
            errors[0].Message);
        Assert.Equal(2, errors.Count);
    }

    // The scope's fields are all a type is given, and an example's content type comes with its definition.
    [Fact]
    public async Task Examples_run_on_the_scoped_fields_with_their_content_type()
    {
        var page = new ContentTypeDefinition([new FieldDefinition("title", "text"), new FieldDefinition("body", "richText")]);
        var definition = new CheckDefinition(
            "sample.definedFields",
            "Fields are defined by the content type.",
            null,
            [
                Example("defined", CheckVerdict.Pass, """{"title": "A", "notes": "outside the scope"}""", "product-page"),
                Example("undefined", CheckVerdict.Fail, """{"subtitle": "B"}""", "product-page"),
            ],
            scope: new CheckScope(Fields: ["title", "subtitle"]));

        var result = await definition.RunExamplesAsync(
            CheckTypes,
            new Dictionary<string, ContentTypeDefinition> { ["product-page"] = page },
            NoServices);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void A_scope_applies_to_its_content_types_and_narrows_to_its_fields()
    {
        var scope = new CheckScope(["product-page"], ["title", "body"]);
        var subject = new CheckSubject(Parse("""{"body": "B", "title": "T", "price": 5}"""), ["s"], "product-page");

        Assert.True(scope.AppliesTo("product-page"));
        Assert.False(scope.AppliesTo("blog-post"));
        Assert.False(scope.AppliesTo(null));
        Assert.True(CheckScope.All.AppliesTo(null));
        Assert.Equal("""{"body":"B","title":"T"}""", Compact(scope.Narrow(subject).Fields));
        Assert.Equal(["s"], scope.Narrow(subject).Sources);
        Assert.Same(subject, CheckScope.All.Narrow(subject));
    }

    [Fact]
    public void A_check_starts_inactive_at_version_one_and_a_revision_keeps_its_active_version()
    {
        var definition = CheckDefinition.Parse(ShortTitles).Value;

        var check = Check.Create(Site.Default.Id, "short-titles", definition, CheckTypes).Value;
        var revised = check.WithActiveVersion(1).Revise(definition, CheckTypes).Value;

        Assert.Equal((1, 1, (int?)null), (check.Version, check.LatestVersion, check.ActiveVersion));
        Assert.Equal((2, 2, (int?)1), (revised.Version, revised.LatestVersion, revised.ActiveVersion));
        Assert.Equal((check.Id, "short-titles"), (revised.Id, revised.Name));

        var refused = Check.Create(Guid.Empty, "Short Titles", new CheckDefinition("sample.maxWords", "x", Parse("""{"max": 1}"""), []), CheckTypes);
        Assert.Equal(
            [CheckErrorCodes.SiteMissing, CheckErrorCodes.NameInvalid, CheckErrorCodes.ExamplesMissing],
            refused.Errors.Select(error => error.Code));
    }

    private static CheckExample Example(string name, CheckVerdict expected, string fields, string? contentType = null)
    {
        return new CheckExample(name, expected, Parse(fields), contentType: contentType);
    }

    private static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string Compact(JsonElement element)
    {
        return JsonSerializer.Serialize(element);
    }

    // Fails every field its content type does not define, so a test sees whether the definition reached the type. Generic, so
    // discovering this assembly's extensions skips it.
    private sealed class DefinedFieldsCheckType<TTag> : ICheckType
    {
        public string Name => "sample.definedFields";

        public CheckKind Kind => CheckKind.Deterministic;

        public IReadOnlyList<FluentCMS.Core.Results.Error> ValidateSettings(JsonElement settings)
        {
            return new CheckSettingsReader(Name, settings).Errors();
        }

        public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
        {
            if (input.Subject.Definition is not { } definition)
            {
                return Task.FromResult(CheckOutcome.NeedsReview("The content type is not known."));
            }

            var findings = input.Subject.Fields.EnumerateObject()
                .Where(property => !definition.HasField(property.Name))
                .Select(property => new CheckFinding($"Remove '{property.Name}', which {input.Subject.ContentType} does not define.", $"/{property.Name}"))
                .ToList();

            return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
        }
    }
}
