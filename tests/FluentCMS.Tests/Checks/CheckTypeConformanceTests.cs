using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

// The contract every check type meets, built in or not (EXT-N1). Derive one class per type: name the type, list settings it must
// accept and settings it must reject, list content it must pass and content it must fail under given settings, and override
// AddServices to register a type that lives outside Core or a service the type reads, such as a model.
public abstract class CheckTypeConformanceTests : IDisposable
{
    private static readonly string[] ValuesOfEveryKind =
        ["null", "true", "false", "0", "-1", "1.5", "1e400", "\"\"", "\"text\"", "[]", "[1, \"a\", null]", "{}", "{\"nested\": {\"deeper\": \"x\"}}"];

    private ServiceProvider? _provider;

    protected abstract string CheckTypeName { get; }

    protected abstract IEnumerable<string> AcceptedSettings { get; }

    protected abstract IEnumerable<string> RejectedSettings { get; }

    // Each pair is settings the type accepts and content it must give that verdict.
    protected abstract IEnumerable<(string Settings, CheckSubject Subject)> Passing { get; }

    protected abstract IEnumerable<(string Settings, CheckSubject Subject)> Failing { get; }

    protected CheckTypeRegistry Registry => Provider.GetRequiredService<CheckTypeRegistry>();

    protected ServiceProvider Provider => _provider ??= BuildProvider();

    protected virtual void AddServices(IServiceCollection services)
    {
    }

    [Fact]
    public void Is_registered_through_the_extension_point_once_under_a_valid_name()
    {
        Assert.True(CheckNames.IsTypeName(CheckTypeName), $"'{CheckTypeName}' is not a valid check type name.");

        var checkType = Registry.Find(CheckTypeName);

        Assert.NotNull(checkType);
        Assert.Equal(CheckTypeName, checkType.Name);
        Assert.True(Enum.IsDefined(checkType.Kind));
        Assert.Single(Provider.GetRequiredService<IExtensionCatalog>().For<ICheckType>(), descriptor => descriptor.Implementation == checkType.GetType());
    }

    [Fact]
    public void Accepts_the_settings_it_documents()
    {
        Assert.NotEmpty(AcceptedSettings);

        foreach (var json in AcceptedSettings)
        {
            var errors = Registry.ValidateSettings(CheckTypeName, Parse(json));

            Assert.True(errors.Count == 0, $"{json} was rejected: {Describe(errors)}");
        }
    }

    [Fact]
    public void Rejects_invalid_settings_with_a_code_and_a_message()
    {
        Assert.NotEmpty(RejectedSettings);

        foreach (var json in RejectedSettings)
        {
            var errors = Registry.ValidateSettings(CheckTypeName, Parse(json));

            Assert.True(errors.Count > 0, $"{json} was accepted.");
            Assert.All(errors, error =>
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Code));
                Assert.False(string.IsNullOrWhiteSpace(error.Message));
            });
        }
    }

    [Fact]
    public void Rejects_a_setting_it_does_not_know()
    {
        foreach (var json in AcceptedSettings)
        {
            var settings = JsonNode.Parse(json)!.AsObject();
            settings["conformanceUnknownSetting"] = true;

            var errors = Registry.ValidateSettings(CheckTypeName, Parse(settings.ToJsonString()));

            Assert.Contains(errors, error => error.Code == SchemaErrorCodes.SettingUnknown && error.Message.Contains("conformanceUnknownSetting"));
        }
    }

    [Fact]
    public async Task Passes_the_content_it_documents()
    {
        Assert.NotEmpty(Passing);

        foreach (var (settings, subject) in Passing)
        {
            var outcome = await EvaluateAsync(settings, subject);

            Assert.True(outcome.Verdict == CheckVerdict.Pass, $"{subject.Fields} under {settings} was {Describe(outcome)}");
            Assert.Empty(outcome.Findings);
        }
    }

    // A failure tells the agent or person exactly what to change, and where (UX-03).
    [Fact]
    public async Task Fails_the_content_it_documents_with_findings_that_say_what_to_change_and_where()
    {
        Assert.NotEmpty(Failing);

        foreach (var (settings, subject) in Failing)
        {
            var outcome = await EvaluateAsync(settings, subject);

            Assert.True(outcome.Verdict == CheckVerdict.Fail, $"{subject.Fields} under {settings} was {Describe(outcome)}");
            Assert.NotEmpty(outcome.Findings);
            Assert.All(outcome.Findings, finding =>
            {
                Assert.False(string.IsNullOrWhiteSpace(finding.Message), $"A finding for {subject.Fields} has no message.");
                Assert.True(Resolves(subject.Fields, finding.Pointer), $"'{finding.Pointer}' does not point into {subject.Fields}.");
            });
        }
    }

    // Content comes from an agent, so a field can hold anything; the answer is an outcome, never an exception.
    [Fact]
    public async Task Never_throws_whatever_the_content_holds()
    {
        var type = Registry.Find(CheckTypeName)!;
        var names = Passing.Concat(Failing)
            .Where(pair => pair.Subject.Fields.ValueKind == JsonValueKind.Object)
            .SelectMany(pair => pair.Subject.Fields.EnumerateObject().Select(property => property.Name))
            .Append("conformanceField")
            .Distinct()
            .ToList();
        var documents = names
            .SelectMany(name => ValuesOfEveryKind.Select(value => $$"""{"{{name}}": {{value}}}"""))
            .Append("{}")
            .ToList();

        foreach (var settings in AcceptedSettings)
        {
            foreach (var fields in documents)
            {
                var input = new CheckInput(Parse(settings), new CheckSubject(Parse(fields), ["  ", "https://example.com"]), Provider);

                var exception = await Record.ExceptionAsync(async () => Assert.NotNull(await type.EvaluateAsync(input, CancellationToken.None)));

                Assert.True(exception is null, $"{fields} under {settings} threw {exception}");
            }
        }
    }

    // A check comes from an agent too, so a setting can hold anything; the answer is an error, never an exception.
    [Fact]
    public void Never_throws_whatever_a_setting_holds()
    {
        foreach (var json in AcceptedSettings.Concat(RejectedSettings))
        {
            var original = JsonNode.Parse(json)!.AsObject();

            foreach (var name in original.Select(property => property.Key).ToList())
            {
                foreach (var value in ValuesOfEveryKind)
                {
                    var settings = original.DeepClone().AsObject();
                    settings[name] = JsonNode.Parse(value);
                    var probe = settings.ToJsonString();

                    var exception = Record.Exception(() => Registry.ValidateSettings(CheckTypeName, Parse(probe)));

                    Assert.True(exception is null, $"{probe} threw {exception}");
                }
            }
        }
    }

    public void Dispose()
    {
        _provider?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    protected static CheckSubject Subject(string fields, params string[] sources)
    {
        return new CheckSubject(Parse(fields), sources);
    }

    protected static CheckSubject Subject(string contentType, ContentTypeDefinition definition, string fields, params string[] sources)
    {
        return new CheckSubject(Parse(fields), sources, contentType, definition);
    }

    private Task<CheckOutcome> EvaluateAsync(string settings, CheckSubject subject)
    {
        var errors = Registry.ValidateSettings(CheckTypeName, Parse(settings));
        Assert.True(errors.Count == 0, $"{settings} was rejected: {Describe(errors)}");

        return Registry.EvaluateAsync(CheckTypeName, new CheckInput(Parse(settings), subject, Provider));
    }

    // An empty pointer is the whole subject; anything else names a place in its fields, with ~1 for '/' and ~0 for '~'.
    private static bool Resolves(JsonElement fields, string pointer)
    {
        if (pointer.Length == 0)
        {
            return true;
        }

        if (!pointer.StartsWith('/'))
        {
            return false;
        }

        var current = fields;
        foreach (var token in pointer[1..].Split('/').Select(token => token.Replace("~1", "/").Replace("~0", "~")))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(token, out var property))
            {
                current = property;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(token, out var index) && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddFluentCmsCore();
        AddServices(services);
        return services.BuildServiceProvider();
    }

    private static string Describe(IEnumerable<Error> errors)
    {
        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}"));
    }

    private static string Describe(CheckOutcome outcome)
    {
        return $"{outcome.Verdict}: {string.Join("; ", outcome.Findings.Select(finding => $"{finding.Pointer} {finding.Message}"))}";
    }
}
