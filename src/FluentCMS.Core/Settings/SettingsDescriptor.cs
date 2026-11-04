using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Settings;
using ValidationError = FluentCMS.Content.ValidationError;
using ValidationException = FluentCMS.Content.ValidationException;

namespace FluentCMS.Core.Settings;

// One settings class: its area, the keys of its document, which of them are secret, and how a document is checked.
internal sealed class SettingsDescriptor
{
    private readonly Lazy<JsonNode> _schema;
    private readonly FrozenDictionary<string, string> _jsonNameOf;

    public SettingsDescriptor(string area, Type type)
    {
        Area = area;
        Type = type;
        Permission = CorePermissions.Settings(area);

        var properties = SettingsJson.Strict.GetTypeInfo(type).Properties.Where(property => property.Get is not null && property.Set is not null).ToList();
        KnownKeys = properties.Select(property => property.Name).ToFrozenSet(StringComparer.Ordinal);
        SecretKeys = properties.Where(IsSecret).Select(property => property.Name).ToFrozenSet(StringComparer.Ordinal);
        _jsonNameOf = properties
            .Select(property => (Member: (property.AttributeProvider as MemberInfo)?.Name, property.Name))
            .Where(pair => pair.Member is not null)
            .ToFrozenDictionary(pair => pair.Member!, pair => pair.Name, StringComparer.Ordinal);
        _schema = new Lazy<JsonNode>(() => SettingsSchema.Create(this, properties));
    }

    public string Area { get; }

    public Type Type { get; }

    public string Permission { get; }

    // The names of the keys of the document, as they are in JSON.
    public IReadOnlySet<string> KnownKeys { get; }

    // The keys whose values are never put in an event, an activity row, a log line or a schema.
    public IReadOnlySet<string> SecretKeys { get; }

    public object CreateDefault() => Activator.CreateInstance(Type)!;

    // The JSON schema of the document, from the type and the attributes of its properties. A new tree every time, so that
    // the caller may change it.
    public JsonNode CreateSchema() => _schema.Value.DeepClone();

    public object Read(string json) => JsonSerializer.Deserialize(json, Type, SettingsJson.Lenient)!;

    // Whether a stored document can still be read by the class as it is now.
    public bool CanRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, Type, SettingsJson.Lenient) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The canonical document of a value that a caller built.
    public string Write(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, Type, SettingsJson.Strict);
        }
        catch (JsonException exception)
        {
            throw Fail(PathOf(exception), SettingsErrorCodes.InvalidValue, "The value is not valid for this settings type.");
        }
    }

    // Checks a document that came from outside and returns it in canonical form: only the keys of the type, every one of
    // them there, and every validation attribute of the type held. Nothing that is wrong is repeated back, because a value
    // may be a secret.
    public string Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (Encoding.UTF8.GetByteCount(json) > SettingsJson.MaxBytes)
        {
            throw Fail("", SettingsErrorCodes.InvalidJson, $"The settings are larger than {SettingsJson.MaxBytes} bytes.");
        }

        CheckKeys(json);

        object? value;
        try
        {
            value = JsonSerializer.Deserialize(json, Type, SettingsJson.Strict);
        }
        catch (JsonException exception)
        {
            throw Fail(PathOf(exception), SettingsErrorCodes.InvalidValue, "The value is not valid for this settings type.");
        }

        return value is null
            ? throw Fail("", SettingsErrorCodes.InvalidJson, "The settings must be a JSON object.")
            : Write(Validated(value));
    }

    // The error of a setting that cannot be written, with the path of the key it is about.
    public static ValidationException Fail(string path, string code, string message) => new([new ValidationError(path, code, message)]);

    private void CheckKeys(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = SettingsJson.MaxDepth });
        }
        catch (JsonException)
        {
            throw Fail("", SettingsErrorCodes.InvalidJson, "The settings are not valid JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Fail("", SettingsErrorCodes.InvalidJson, "The settings must be a JSON object.");
            }

            var unknown = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !KnownKeys.Contains(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new ValidationError(name, SettingsErrorCodes.UnknownKey, $"The settings area '{Area}' has no key '{name}'."))
                .ToList();

            if (unknown.Count > 0)
            {
                throw new ValidationException(unknown);
            }
        }
    }

    private object Validated(object value)
    {
        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true))
        {
            return value;
        }

        List<ValidationError> errors = [];
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [""];
            foreach (var member in members)
            {
                var key = _jsonNameOf.GetValueOrDefault(member, member);
                var message = SecretKeys.Contains(key) ? "The value is not valid." : result.ErrorMessage ?? "The value is not valid.";
                errors.Add(new ValidationError(key, SettingsErrorCodes.InvalidValue, message));
            }
        }

        throw new ValidationException(errors);
    }

    // "$.limits.port" is the key limits.port.
    private static string PathOf(JsonException exception) =>
        exception.Path is { Length: > 1 } path ? path[(path.StartsWith("$.", StringComparison.Ordinal) ? 2 : 1)..] : "";

    private static bool IsSecret(JsonPropertyInfo property) =>
        SecretScrubber.NamesSecret(property.Name)
        || (property.AttributeProvider?.IsDefined(typeof(SettingsSecretAttribute), inherit: true) ?? false);
}
