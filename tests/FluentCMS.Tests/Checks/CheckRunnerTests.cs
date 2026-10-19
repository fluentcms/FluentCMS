using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

public sealed class CheckRunnerTests : IDisposable
{
    private readonly FakeModelGateway _gateway = new();
    private readonly ActiveChecks _active = new();
    private readonly ServiceProvider _provider;

    public CheckRunnerTests()
    {
        _provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddSingleton<IModelGateway>(_gateway)
            .AddSingleton<ICheckDirectory>(_active)
            .BuildServiceProvider();
        var checkTypes = _provider.GetRequiredService<CheckTypeRegistry>();

        _active.Add(checkTypes, "calm-voice", Definition("tone", """{"voice": "Calm."}""", """{"body": "Calm."}""", """{"body": "Loud!!!"}"""));
        _active.Add(checkTypes, "no-superlatives", Definition(
            "terms",
            """{"terms": ["best in class"]}""",
            """{"headline": "A kettle"}""",
            """{"headline": "Best in class"}""",
            scope: new CheckScope(["product-page"])));
        _active.Add(checkTypes, "short-headline", Definition(
            "length",
            """{"max": 10}""",
            """{"headline": "A kettle"}""",
            """{"headline": "A kettle with a long name"}""",
            CheckSeverity.Warn,
            new CheckScope(Fields: ["headline"])));
    }

    private ICheckRunner Runner => _provider.GetRequiredService<ICheckRunner>();

    // CHK-03: each active check gives pass, warn or fail, with its evidence; a warning check's failure only warns.
    [Fact]
    public async Task Every_active_check_that_applies_gives_its_status_and_evidence_in_name_order()
    {
        var results = await Runner.RunAsync(Subject("product-page", """{"headline": "The best-in-class kettle", "body": "BUY NOW!!!"}"""));

        Assert.Equal(["calm-voice", "no-superlatives", "short-headline"], results.Select(result => result.Check));
        Assert.Equal([CheckStatus.Fail, CheckStatus.Fail, CheckStatus.Warn], results.Select(result => result.Status));
        Assert.Equal([CheckKind.LlmJudged, CheckKind.Deterministic, CheckKind.Deterministic], results.Select(result => result.Kind));
        Assert.Equal(("/body", "Drop the exclamation marks."), (results[0].Findings[0].Pointer, results[0].Findings[0].Message));
        Assert.NotNull(results[0].Model);
        Assert.Null(results[1].Model);
        Assert.Equal(("terms", 1, CheckSeverity.Block, "The terms check."), (results[1].Type, results[1].Version, results[1].Severity, results[1].Description));
        Assert.Equal(CheckStatus.Fail, CheckResult.Overall(results));
    }

    [Fact]
    public async Task A_check_runs_only_on_the_content_types_and_fields_its_scope_names()
    {
        var page = await Runner.RunAsync(Subject("blog-post", """{"headline": "Best in class", "body": "Fine."}"""));
        var untyped = await Runner.RunAsync(Subject(null, """{"headline": "Best in class"}"""));

        Assert.Equal(["calm-voice", "short-headline"], page.Select(result => result.Check));
        Assert.Equal(CheckStatus.Warn, page[1].Status);
        Assert.Equal(["calm-voice", "short-headline"], untyped.Select(result => result.Check));
    }

    [Fact]
    public async Task Deterministic_checks_alone_ask_no_model()
    {
        var results = await Runner.RunAsync(Subject("product-page", """{"headline": "A kettle", "body": "Loud!!!"}"""), CheckSelection.Deterministic);

        Assert.Equal(["no-superlatives", "short-headline"], results.Select(result => result.Check));
        Assert.All(results, result => Assert.Equal(CheckStatus.Pass, result.Status));
        Assert.Equal(CheckStatus.Pass, CheckResult.Overall(results));
        Assert.Empty(_gateway.Requests);
    }

    // MDL-03: without a model an LLM-judged check needs review, which a pass elsewhere does not hide.
    [Fact]
    public async Task Without_a_model_the_run_needs_review_unless_something_fails()
    {
        _gateway.Available = false;

        var calm = await Runner.RunAsync(Subject("product-page", """{"headline": "A kettle", "body": "Calm."}"""));
        var loud = await Runner.RunAsync(Subject("product-page", """{"headline": "Best in class", "body": "Calm."}"""));

        Assert.Equal(CheckStatus.NeedsReview, calm[0].Status);
        Assert.Equal(CheckStatus.NeedsReview, CheckResult.Overall(calm));
        Assert.Equal(CheckStatus.Fail, CheckResult.Overall(loud));
    }

    [Theory]
    [InlineData(CheckVerdict.Pass, CheckSeverity.Block, CheckStatus.Pass)]
    [InlineData(CheckVerdict.Fail, CheckSeverity.Block, CheckStatus.Fail)]
    [InlineData(CheckVerdict.Fail, CheckSeverity.Warn, CheckStatus.Warn)]
    [InlineData(CheckVerdict.NeedsReview, CheckSeverity.Warn, CheckStatus.NeedsReview)]
    public void A_verdict_and_a_severity_give_a_status(CheckVerdict verdict, CheckSeverity severity, CheckStatus expected)
    {
        Assert.Equal(expected, CheckResult.StatusOf(verdict, severity));
    }

    [Fact]
    public void No_results_pass()
    {
        Assert.Equal(CheckStatus.Pass, CheckResult.Overall([]));
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static CheckSubject Subject(string? contentType, string fields)
    {
        return new CheckSubject(Parse(fields), [], contentType);
    }

    private static CheckDefinition Definition(
        string type,
        string settings,
        string passing,
        string failing,
        CheckSeverity severity = CheckSeverity.Block,
        CheckScope? scope = null)
    {
        var contentType = scope?.ContentTypes?[0];
        return new CheckDefinition(
            type,
            $"The {type} check.",
            Parse(settings),
            [
                new CheckExample("passes", CheckVerdict.Pass, Parse(passing), contentType: contentType),
                new CheckExample("fails", CheckVerdict.Fail, Parse(failing), contentType: contentType),
            ],
            severity,
            scope);
    }

    private static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private sealed class ActiveChecks : ICheckDirectory
    {
        private readonly List<Check> _checks = [];

        public void Add(CheckTypeRegistry checkTypes, string name, CheckDefinition definition)
        {
            _checks.Add(Check.Create(Site.Default.Id, name, definition, checkTypes).Value.WithActiveVersion(1));
        }

        public Task<IReadOnlyList<Check>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Check>>(_checks.OrderBy(check => check.Name, StringComparer.Ordinal).ToList());
        }

        public Task<IReadOnlyList<CheckSummary>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Check>> GetAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Check>> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Check>> CreateAsync(NewCheck check, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Check>> ReviseAsync(string name, int baseVersion, CheckDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<Check>> ActivateAsync(string name, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Check>> DeactivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
