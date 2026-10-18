using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

// The contract every field type meets, built in or not (EXT-N1). Derive one class per type: name the type, list settings it must
// accept and settings it must reject, and override AddFieldTypes to register a type that lives outside Core.
public abstract class FieldTypeConformanceTests : IDisposable
{
    private static readonly string[] ValuesOfEveryKind =
        ["null", "true", "false", "0", "-1", "1.5", "1e400", "\"\"", "\"text\"", "[]", "[1, \"a\", null]", "{}", "{\"nested\": {}}"];

    private ServiceProvider? _provider;

    protected abstract string FieldTypeName { get; }

    protected abstract IEnumerable<string> AcceptedSettings { get; }

    protected abstract IEnumerable<string> RejectedSettings { get; }

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
