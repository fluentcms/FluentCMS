using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

public sealed class CheckTypeRegistryTests
{
    private static readonly IServiceProvider NoServices = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Core_registers_the_registry_from_the_extension_point()
    {
        using var provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddFluentCmsExtension<ICheckType, MaxWordsCheckType>()
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<CheckTypeRegistry>(), provider.GetRequiredService<CheckTypeRegistry>());
        Assert.IsType<MaxWordsCheckType>(provider.GetRequiredService<CheckTypeRegistry>().Find("sample.maxWords"));
    }

    [Fact]
    public void Two_check_types_with_the_same_name_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new CheckTypeRegistry([new StubCheckType<object>("sample.maxWords"), new MaxWordsCheckType()]));

        Assert.Contains("'sample.maxWords'", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Terms")]
    [InlineData("term list")]
    [InlineData("acme.")]
    [InlineData("acme..tone")]
    public void A_check_type_with_an_invalid_name_is_rejected(string name)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new CheckTypeRegistry([new StubCheckType<object>(name)]));

        Assert.Contains("not a valid check type name", exception.Message);
    }

    [Fact]
    public void A_check_type_of_an_unknown_kind_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new CheckTypeRegistry([new StubCheckType<object>("sample.odd") { Kind = (CheckKind)7 }]));

        Assert.Contains("not a check kind", exception.Message);
    }

    [Fact]
    public void An_unknown_type_is_reported_with_the_known_types()
    {
        var registry = new CheckTypeRegistry([new StubCheckType<object>("sample.b"), new StubCheckType<object>("sample.a")]);

        var error = Assert.Single(registry.ValidateSettings("sample.c", Parse("{}")));

        Assert.Equal(CheckErrorCodes.CheckTypeUnknown, error.Code);
        Assert.Equal("There is no check type 'sample.c'; the known types are sample.a, sample.b.", error.Message);
        Assert.Equal(
            "There is no check type 'sample.c'; no check types are installed.",
            Assert.Single(new CheckTypeRegistry([]).ValidateSettings("sample.c", Parse("{}"))).Message);
    }

    [Fact]
    public void Settings_that_are_not_an_object_are_refused_before_the_type_reads_them()
    {
        var registry = new CheckTypeRegistry([new StubCheckType<object>("sample.a") { Settings = _ => throw new InvalidOperationException() }]);

        var error = Assert.Single(registry.ValidateSettings("sample.a", Parse("[]")));

        Assert.Equal(SchemaErrorCodes.SettingInvalid, error.Code);
    }

    // Content must never pass because its check could not run (MDL-03 degrades to needs review, and so does everything else).
    [Fact]
    public async Task A_type_that_is_not_installed_needs_review()
    {
        var outcome = await new CheckTypeRegistry([]).EvaluateAsync("sample.gone", Input());

        Assert.Equal(CheckVerdict.NeedsReview, outcome.Verdict);
        Assert.Equal("The check type 'sample.gone' is not installed, so the check could not run.", Assert.Single(outcome.Findings).Message);
    }

    [Fact]
    public async Task A_type_that_throws_needs_review_and_says_why()
    {
        var registry = new CheckTypeRegistry(
        [
            new StubCheckType<object>("sample.broken") { Evaluate = _ => throw new FormatException("The pattern is broken.") },
            new StubCheckType<object>("sample.timeout") { Evaluate = _ => throw new TaskCanceledException("The request timed out.") },
            new StubCheckType<object>("sample.empty") { Evaluate = _ => Task.FromResult<CheckOutcome>(null!) },
        ]);

        var broken = await registry.EvaluateAsync("sample.broken", Input());
        var timeout = await registry.EvaluateAsync("sample.timeout", Input());
        var empty = await registry.EvaluateAsync("sample.empty", Input());

        Assert.Equal(CheckVerdict.NeedsReview, broken.Verdict);
        Assert.Equal("The check type 'sample.broken' failed while running: The pattern is broken.", Assert.Single(broken.Findings).Message);
        Assert.Equal(CheckVerdict.NeedsReview, timeout.Verdict);
        Assert.Equal(CheckVerdict.NeedsReview, empty.Verdict);
    }

    [Fact]
    public async Task Cancelling_the_caller_stops_the_evaluation_instead_of_reading_as_needs_review()
    {
        using var cancellation = new CancellationTokenSource();
        var registry = new CheckTypeRegistry(
        [
            new StubCheckType<object>("sample.slow")
            {
                Evaluate = async token =>
                {
                    await cancellation.CancelAsync();
                    token.ThrowIfCancellationRequested();
                    return CheckOutcome.Pass();
                },
            },
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.EvaluateAsync("sample.slow", Input(), cancellation.Token));
    }

    [Fact]
    public void A_failure_says_what_to_change()
    {
        Assert.Throws<ArgumentException>(() => CheckOutcome.Fail([]));
        Assert.Throws<ArgumentException>(() => CheckOutcome.NeedsReview(" "));
        Assert.Equal(new CheckFinding("Add alt text.", "/body"), Assert.Single(CheckOutcome.Fail("Add alt text.", "/body").Findings));
    }

    private static CheckInput Input()
    {
        return new CheckInput(Parse("{}"), new CheckSubject(Parse("{}"), []), NoServices);
    }

    private static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    // Generic, so discovering this assembly's extensions skips it.
    private sealed class StubCheckType<TTag>(string name) : ICheckType
    {
        public string Name { get; } = name;

        public CheckKind Kind { get; init; } = CheckKind.Deterministic;

        public Func<JsonElement, IReadOnlyList<Error>> Settings { get; init; } = _ => [];

        public Func<CancellationToken, Task<CheckOutcome>> Evaluate { get; init; } = _ => Task.FromResult(CheckOutcome.Pass());

        public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
        {
            return Settings(settings);
        }

        public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
        {
            return Evaluate(cancellationToken);
        }
    }
}
