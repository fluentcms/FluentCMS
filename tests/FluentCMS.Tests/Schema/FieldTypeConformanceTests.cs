using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

// The contract every field type meets, built in or not (EXT-N1). Derive one class per type: name the type, list settings it must
// accept and settings it must reject, list values it must accept and reject under given settings, and override AddFieldTypes to
// register a type that lives outside Core.
public abstract class FieldTypeConformanceTests : IDisposable
{
    private static readonly string[] ValuesOfEveryKind =
        ["null", "true", "false", "0", "-1", "1.5", "1e400", "\"\"", "\"text\"", "[]", "[1, \"a\", null]", "{}", "{\"nested\": {}}"];

    private ServiceProvider? _provider;

    protected abstract string FieldTypeName { get; }

    protected abstract IEnumerable<string> AcceptedSettings { get; }

    protected abstract IEnumerable<string> RejectedSettings { get; }

    // Each pair is settings the type accepts and a value as an entry stores it.
    protected abstract IEnumerable<(string Settings, string Value)> AcceptedValues { get; }

    protected abstract IEnumerable<(string Settings, string Value)> RejectedValues { get; }

    protected FieldTypeRegistry Registry => Provider.GetRequiredService<FieldTypeRegistry>();

    private ServiceProvider Provider => _provider ??= BuildProvider();

    protected virtual void AddFieldTypes(IServiceCollection services)
    {
    }

    [Fact]
    public void Is_registered_through_the_extension_point_once_under_a_valid_name()
    {
        Assert.True(SchemaNames.IsFieldTypeName(FieldTypeName), $"'{FieldTypeName}' is not a valid field type name.");

        var fieldType = Registry.Find(FieldTypeName);

        Assert.NotNull(fieldType);
        Assert.Equal(FieldTypeName, fieldType.Name);
        Assert.Single(Provider.GetRequiredService<IExtensionCatalog>().For<IFieldType>(), descriptor => descriptor.Implementation == fieldType.GetType());
    }

    [Fact]
    public void Accepts_the_settings_it_documents()
    {
        Assert.NotEmpty(AcceptedSettings);

        foreach (var json in AcceptedSettings)
        {
            var errors = Registry.ValidateSettings(FieldTypeName, Parse(json));

            Assert.True(errors.Count == 0, $"{json} was rejected: {Describe(errors)}");
        }
    }

    [Fact]
    public void Rejects_invalid_settings_with_a_code_and_a_message()
    {
        foreach (var json in RejectedSettings)
        {
            var errors = Registry.ValidateSettings(FieldTypeName, Parse(json));

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

            var errors = Registry.ValidateSettings(FieldTypeName, Parse(settings.ToJsonString()));

            Assert.Contains(errors, error => error.Code == SchemaErrorCodes.SettingUnknown && error.Message.Contains("conformanceUnknownSetting"));
        }
    }

    [Fact]
    public void Declares_reference_targets_as_a_slug_and_an_optional_field_name()
    {
        foreach (var json in AcceptedSettings)
        {
            foreach (var target in Registry.ReferenceTargets(FieldTypeName, Parse(json)))
            {
                Assert.True(SchemaNames.IsSlug(target.Type), $"{json} declared the type '{target.Type}', which is not a slug.");
                Assert.True(
                    target.Field is null || SchemaNames.IsFieldName(target.Field),
                    $"{json} declared the field '{target.Field}', which is not a field name.");
            }
        }
    }

    // The schema document hands this example to agents as a valid value (SCH-06).
    [Fact]
    public async Task Describes_its_values_with_a_schema_its_own_example_satisfies()
    {
        foreach (var json in AcceptedSettings)
        {
            var value = Registry.DescribeValue(FieldTypeName, Parse(json));
            var errors = await JsonSchemaValidation.ErrorsAsync(value.Schema, value.Example);

            Assert.False(string.IsNullOrWhiteSpace(value.Description), $"{json} has no description.");
            Assert.True(
                errors.Count == 0,
                $"{json}: {value.Example?.ToJsonString() ?? "null"} does not satisfy {value.Schema.ToJsonString()}: {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void Accepts_the_values_it_documents()
    {
        Assert.NotEmpty(AcceptedValues);

        foreach (var (settings, value) in AcceptedValues)
        {
            var errors = Registry.ValidateValue(FieldTypeName, Parse(settings), Parse(value));

            Assert.True(errors.Count == 0, $"{value} under {settings} was rejected: {Describe(errors)}");
        }
    }

    [Fact]
    public void Rejects_invalid_values_with_value_invalid_and_a_message()
    {
        foreach (var (settings, value) in RejectedValues)
        {
            var errors = Registry.ValidateValue(FieldTypeName, Parse(settings), Parse(value));

            Assert.True(errors.Count > 0, $"{value} under {settings} was accepted.");
            Assert.All(errors, error =>
            {
                Assert.Equal(SchemaErrorCodes.ValueInvalid, error.Code);
                Assert.False(string.IsNullOrWhiteSpace(error.Message));
            });
        }
    }

    // An agent writes entries from the schema document's examples (SCH-06), so each one must be a value the type stores.
    [Fact]
    public void Accepts_the_example_it_describes()
    {
        foreach (var json in AcceptedSettings)
        {
            var settings = Parse(json);
            var example = Registry.DescribeValue(FieldTypeName, settings).Example;
            var errors = Registry.ValidateValue(FieldTypeName, settings, Parse(example?.ToJsonString() ?? "null"));

            Assert.True(errors.Count == 0, $"{json}: the example {example?.ToJsonString()} was rejected: {Describe(errors)}");
        }
    }

    // Where used and resolved views read the reference index (CNT-05, ADR-0005), so a type that points somewhere must say where
    // every value points, and a type that declares no targets points at no entry.
    [Fact]
    public void Lists_the_references_of_its_example_against_the_targets_it_declares()
    {
        foreach (var json in AcceptedSettings)
        {
            var settings = Parse(json);
            var targets = Registry.ReferenceTargets(FieldTypeName, settings);
            var example = Parse(Registry.DescribeValue(FieldTypeName, settings).Example?.ToJsonString() ?? "null");

            var entries = Registry.References(FieldTypeName, settings, example).Where(reference => reference.Kind == ReferenceKind.Entry).ToList();

            if (targets.Count > 0)
            {
                Assert.True(entries.Count > 0, $"{json} declares targets but its example lists no reference.");
            }

            Assert.All(entries, reference =>
            {
                Assert.Contains(reference.Target, targets);
                Assert.True(reference.Pointer.Length == 0 || reference.Pointer.StartsWith('/'), $"'{reference.Pointer}' is not a JSON Pointer.");
            });
        }
    }

    // A value comes from an agent too; the answer is an error, never an exception.
    [Fact]
    public void Never_throws_whatever_a_value_holds()
    {
        foreach (var json in AcceptedSettings)
        {
            foreach (var value in ValuesOfEveryKind.Where(value => value != "null"))
            {
                var exception = Record.Exception(() => Registry.ValidateValue(FieldTypeName, Parse(json), Parse(value)));

                Assert.True(exception is null, $"{value} under {json} threw {exception}");
            }
        }
    }

    // A definition comes from an agent, so a setting can hold anything; the answer is an error, never an exception.
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

                    var exception = Record.Exception(() => Registry.ValidateSettings(FieldTypeName, Parse(probe)));

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

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddFluentCmsCore();
        AddFieldTypes(services);
        return services.BuildServiceProvider();
    }

    private static string Describe(IEnumerable<Error> errors)
    {
        return string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}"));
    }
}
