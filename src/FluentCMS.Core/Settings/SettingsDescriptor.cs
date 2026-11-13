using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
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
            throw Fail(Safe(PathOf(exception)), SettingsErrorCodes.InvalidValue, "The value is not valid for this settings type.");
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
            throw Fail(Safe(PathOf(exception)), SettingsErrorCodes.InvalidValue, "The value is not valid for this settings type.");
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

        // A message of an attribute may say what it found, and a path may hold a key of a dictionary. For a secret neither does.
        throw new ValidationException(errors.Select(error => SecretKeys.Contains(RootOf(error.Path))
            ? error with { Path = RootOf(error.Path), Message = "The value is not valid." }
            : error));
    }

    // A path under a secret is the secret's own key: what lies below it may be as secret as the value.
    private string Safe(string path) => SecretKeys.Contains(RootOf(path)) ? RootOf(path) : path;

    // When a document is written without a secret key, the secret stays as it is in the stored document: a form of the admin
    // never shows a secret, so a form that is sent back must not wipe it. A key that is sent, even as null, replaces it. A
    // stored secret that the class would no longer accept, written under an older shape, is not kept: it goes back to its
    // code default, and the key is returned so that the caller can say so. The value is never looked at by the caller.
    public string KeepSecrets(string json, string? stored, out IReadOnlyList<string> dropped)
    {
        dropped = [];
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

            List<string> lost = [];
            foreach (var key in SecretKeys.Order(StringComparer.Ordinal).Where(key => !target.ContainsKey(key) && source[key] is not null))
            {
                if (StillAcceptable(key, source[key]!))
                {
                    target[key] = source[key]!.DeepClone();
                }
                else
                {
                    lost.Add(key);
                }
            }

            dropped = lost;
            return target.ToJsonString();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return json;
        }
    }

    // The secret keys that a row the class cannot read holds, and the document being written does not name: they are lost with
    // the row, which the caller is told by key and never by value.
    public IReadOnlyList<string> SecretsLeftOut(string json, string skippedRow)
    {
        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = SettingsJson.MaxDepth }) is not JsonObject target || JsonNode.Parse(skippedRow) is not JsonObject source)
            {
                return [];
            }

            return [.. SecretKeys.Order(StringComparer.Ordinal).Where(key => !target.ContainsKey(key) && source[key] is not null)];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return [];
        }
    }

    // Whether the value of a secret key reads into the class and holds its validation attributes, by itself: a problem of
    // another key does not count against it.
    private bool StillAcceptable(string key, JsonNode value)
    {
        var probe = new JsonObject { [key] = value.DeepClone() };
        try
        {
            Canonicalize(probe.ToJsonString());
            return true;
        }
        catch (ValidationException exception)
        {
            return !exception.Errors.Any(error => RootOf(error.Path) == key);
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
                    $"[SettingsSecret] is on '{here}', which is not a property of the settings class itself; only those can be secret. Move the secret to the top level of the settings class.");
            }

            if (NamesSecret(property))
            {
                throw new FluentCmsConfigurationException(
                    SettingsErrorCodes.InvalidRegistration,
                    $"'{here}' is named like a secret but is not a property of the settings class itself, and only those are kept out of reads, events and schemas. Move it to the top level of the settings class, or give it a name that does not suggest a secret.");
            }

            RejectNestedSecrets(property.PropertyType, here, seen);
        }
    }

    // The type of the items of a list or the values of a dictionary, until it is neither or comes back to a type it has
    // passed, as one that enumerates itself does.
    private static Type Peel(Type type)
    {
        HashSet<Type> seen = [];
        while (seen.Add(type))
        {
            var next = Nullable.GetUnderlyingType(type) ?? ItemTypeOf(type);
            if (next is null)
            {
                return type;
            }

            type = next;
        }

        return type;
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
        NamesSecret(property)
        || (property.AttributeProvider?.IsDefined(typeof(SettingsSecretAttribute), inherit: true) ?? false);

    // By the name in JSON and by the name in C#, since [JsonPropertyName] must not hide a secret from the rule, and only for a
    // value that can be text.
    private static bool NamesSecret(JsonPropertyInfo property) =>
        SettingsSecretNames.AppliesTo(property.PropertyType)
        && (SettingsSecretNames.Matches(property.Name)
            || ((property.AttributeProvider as MemberInfo)?.Name is { } member && SettingsSecretNames.Matches(member)));
}
