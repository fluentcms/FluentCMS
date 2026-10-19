using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

// A check type outside Core goes through the same doors as a built-in: discovery, the registry and a check definition (EXT-N1).
public sealed class PluginCheckTypeTests
{
    [Fact]
    public async Task A_check_type_from_another_assembly_registers_and_runs_like_a_built_in()
    {
        using var provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddFluentCmsExtensions(typeof(MaxWordsCheckType).Assembly)
            .BuildServiceProvider();
        var checkTypes = provider.GetRequiredService<CheckTypeRegistry>();

        var descriptor = Assert.Single(provider.GetRequiredService<IExtensionCatalog>().For<ICheckType>(), d => d.Implementation == typeof(MaxWordsCheckType));
        Assert.False(descriptor.IsBuiltIn);
        Assert.Contains("sample.maxWords", checkTypes.Names);

        var outcome = await checkTypes.EvaluateAsync(
            "sample.maxWords",
            new CheckInput(Parse("""{"max": 2}"""), new CheckSubject(Parse("""{"title": "One two three"}"""), []), provider));

        Assert.Equal(CheckVerdict.Fail, outcome.Verdict);
        Assert.Equal(new CheckFinding("Shorten this to at most 2 words; it has 3.", "/title", "One two three"), Assert.Single(outcome.Findings));
    }

    [Fact]
    public void Without_its_assembly_the_type_is_unknown()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

        var error = Assert.Single(provider.GetRequiredService<CheckTypeRegistry>().ValidateSettings("sample.maxWords", Parse("{}")));

        Assert.Equal(CheckErrorCodes.CheckTypeUnknown, error.Code);
    }

    [Fact]
    public void Setting_errors_name_the_check_type()
    {
        var errors = new MaxWordsCheckType().ValidateSettings(Parse("""{"max": 0, "maxs": 3}"""));

        Assert.Equal(
            [
                "The setting 'max' must be a whole number greater than zero.",
                "The 'sample.maxWords' check type has no setting 'maxs'; its settings are max.",
            ],
            errors.Select(error => error.Message));
    }

    private static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}

public sealed class MaxWordsCheckTypeTests : CheckTypeConformanceTests
{
    protected override string CheckTypeName => "sample.maxWords";

    protected override IEnumerable<string> AcceptedSettings => ["""{"max": 3}"""];

    protected override IEnumerable<string> RejectedSettings => ["{}", """{"max": 0}""", """{"max": "3"}"""];

    protected override IEnumerable<(string Settings, CheckSubject Subject)> Passing =>
    [
        ("""{"max": 3}""", Subject("""{"title": "Three short words", "price": 12}""")),
        ("""{"max": 1}""", Subject("{}")),
    ];

    protected override IEnumerable<(string Settings, CheckSubject Subject)> Failing =>
    [
        ("""{"max": 3}""", Subject("""{"title": "Four words is too many"}""")),
        ("""{"max": 1}""", Subject("""{"tags": ["fine", "not fine"]}""")),
    ];

    protected override void AddServices(IServiceCollection services)
    {
        services.AddFluentCmsExtensions(typeof(MaxWordsCheckType).Assembly);
    }
}

// What a plugin check type looks like: outside FluentCMS.Core.Builtin, using only what Core makes public.
public sealed class MaxWordsCheckType : ICheckType
{
    public string Name => "sample.maxWords";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        if (settings.ValueKind == JsonValueKind.Object && !settings.TryGetProperty("max", out _))
        {
            reader.Invalid("max", "is required");
        }

        reader.PositiveInteger("max");
        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var max = new CheckSettingsReader(Name, input.Settings).PositiveInteger("max") ?? 1;
        var findings = new List<CheckFinding>();
        Collect(input.Subject.Fields, string.Empty, max, findings);

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static void Collect(JsonElement value, string pointer, int max, List<CheckFinding> findings)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    Collect(property.Value, $"{pointer}/{property.Name.Replace("~", "~0").Replace("/", "~1")}", max, findings);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Collect(item, $"{pointer}/{index++}", max, findings);
                }

                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                if (words > max)
                {
                    findings.Add(new CheckFinding($"Shorten this to at most {max} words; it has {words}.", pointer, text));
                }

                break;
        }
    }
}
