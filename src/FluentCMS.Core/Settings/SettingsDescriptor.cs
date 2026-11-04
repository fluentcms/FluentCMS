using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Settings;
using ValidationError = FluentCMS.Content.ValidationError;
using ValidationException = FluentCMS.Content.ValidationException;

namespace FluentCMS.Core.Settings;

// One settings class: its area, the keys of its document, which of them are secret, and how a document is checked.
internal sealed class SettingsDescriptor
{
    private readonly Lazy<JsonNode> _schema;

    public SettingsDescriptor(string area, Type type)
    {
        Area = area;
        Type = type;
        Permission = CorePermissions.Settings(area);

        var properties = SettingsJson.Strict.GetTypeInfo(type).Properties.Where(property => property.Get is not null && property.Set is not null).ToList();
        KnownKeys = properties.Select(property => property.Name).ToFrozenSet(StringComparer.Ordinal);
        SecretKeys = properties.Where(IsSecret).Select(property => property.Name).ToFrozenSet(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            RejectNestedSecrets(property.PropertyType, property.Name, [type]);
        }

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
        var errors = SettingsValidator.Validate(value, SettingsErrorCodes.InvalidValue);
        if (errors.Count == 0)
        {
            return value;
        }

        // A message of an attribute may say what it found. For a secret it says nothing.
        throw new ValidationException(errors.Select(error => SecretKeys.Contains(RootOf(error.Path)) ? error with { Message = "The value is not valid." } : error));
    }

    // When a document is written without a secret key, the secret stays as it is in the row being replaced: a form of the
    // admin never shows a secret, so a form that is sent back must not wipe it. A key that is sent, even as null, replaces it.
    public string KeepSecrets(string json, string? stored)
    {
        if (SecretKeys.Count == 0 || stored is null)
        {
            return json;
        }

        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = SettingsJson.MaxDepth }) is not JsonObject target || JsonNode.Parse(stored) is not JsonObject source)
            {
                return json;
            }

            foreach (var key in SecretKeys.Where(key => !target.ContainsKey(key) && source[key] is not null))
            {
                target[key] = source[key]!.DeepClone();
            }

            return target.ToJsonString();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return json;
        }
    }

    // The document without its secret keys, and the secret keys that hold a value in it.
    public (string Json, IReadOnlyList<string> SecretsSet) Redact(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        List<string> set = [];
        foreach (var key in SecretKeys.Order(StringComparer.Ordinal))
        {
            if (document.TryGetPropertyValue(key, out var value))
            {
                if (value is not null && !(value is JsonValue text && text.TryGetValue<string>(out var content) && content.Length == 0))
                {
                    set.Add(key);
                }

                document.Remove(key);
            }
        }

        return (document.ToJsonString(), set);
    }

    // A secret is a property of the top level. Below it a value may be shown and written like any other, so the attribute
    // there would promise what the document does not keep.
    private static void RejectNestedSecrets(Type type, string path, HashSet<Type> seen)
    {
        type = Peel(type);
        if (!seen.Add(type))
        {
            return;
        }

        JsonTypeInfo info;
        try
        {
            info = SettingsJson.Strict.GetTypeInfo(type);
        }
        catch (NotSupportedException)
        {
            return;
        }

        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            var here = path + "." + property.Name;
            if (property.AttributeProvider?.IsDefined(typeof(SettingsSecretAttribute), inherit: true) ?? false)
            {
                throw new FluentCmsConfigurationException(
                    SettingsErrorCodes.InvalidRegistration,
                    $"[SettingsSecret] is on '{here}', which is not a property of the settings class itself; only those can be secret. Mark the property of the class that holds it.");
            }

            RejectNestedSecrets(property.PropertyType, here, seen);
        }
    }

    // The type of the items of a list or the values of a dictionary, until it is neither.
    private static Type Peel(Type type)
    {
        while (true)
        {
            var next = Nullable.GetUnderlyingType(type) ?? ItemTypeOf(type);
            if (next is null)
            {
                return type;
            }

            type = next;
        }
    }

    private static Type? ItemTypeOf(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var interfaces = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        var dictionary = interfaces.FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary is not null)
        {
            return dictionary.GetGenericArguments()[1];
        }

        return interfaces.FirstOrDefault(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    // The key at the top of a path: limits for limits.port and many[2].port.
    private static string RootOf(string path)
    {
        var end = path.AsSpan().IndexOfAny('.', '[');
        return end < 0 ? path : path[..end];
    }

    // "$.limits.port" is the key limits.port.
    private static string PathOf(JsonException exception) =>
        exception.Path is { Length: > 1 } path ? path[(path.StartsWith("$.", StringComparison.Ordinal) ? 2 : 1)..] : "";

    private static bool IsSecret(JsonPropertyInfo property) =>
        SecretScrubber.NamesSecret(property.Name)
        || (property.AttributeProvider?.IsDefined(typeof(SettingsSecretAttribute), inherit: true) ?? false);
}
