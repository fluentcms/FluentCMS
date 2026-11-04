using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using ValidationError = FluentCMS.Content.ValidationError;

namespace FluentCMS.Core.Settings;

// The validation attributes of a settings document, checked on the object the document was read into and on every object
// below it: nested objects, the items of lists and the values of dictionaries. A path is the JSON key of what failed,
// such as limits.port or many[2].port. Nothing here reads a value into a message.
internal static class SettingsValidator
{
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, string>> JsonNames = new();

    public static List<ValidationError> Validate(object value, string code)
    {
        List<ValidationError> errors = [];
        Walk(value, "", 0, code, errors);
        return errors;
    }

    private static void Walk(object value, string path, int depth, string code, List<ValidationError> errors)
    {
        if (depth > SettingsJson.MaxDepth)
        {
            return;
        }

        var info = SettingsJson.Strict.GetTypeInfo(value.GetType());
        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                Check(value, path, code, errors);
                foreach (var property in info.Properties)
                {
                    if (property.Get?.Invoke(value) is { } child)
                    {
                        Walk(child, Join(path, property.Name), depth + 1, code, errors);
                    }
                }

                break;

            case JsonTypeInfoKind.Dictionary when value is IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value is { } child)
                    {
                        Walk(child, Join(path, Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? ""), depth + 1, code, errors);
                    }
                }

                break;

            case JsonTypeInfoKind.Enumerable when value is IEnumerable items:
                var index = 0;
                foreach (var item in items)
                {
                    if (item is not null)
                    {
                        Walk(item, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path}[{index}]"), depth + 1, code, errors);
                    }

                    index++;
                }

                break;
        }
    }

    private static void Check(object value, string path, string code, List<ValidationError> errors)
    {
        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true))
        {
            return;
        }

        var names = JsonNames.GetOrAdd(value.GetType(), NamesOf);
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [""];
            foreach (var member in members)
            {
                var key = member.Length == 0 ? path : Join(path, names.GetValueOrDefault(member, member));
                errors.Add(new ValidationError(key, code, result.ErrorMessage ?? "The value is not valid."));
            }
        }
    }

    private static FrozenDictionary<string, string> NamesOf(Type type) =>
        SettingsJson.Strict.GetTypeInfo(type).Properties
            .Select(property => (Member: (property.AttributeProvider as MemberInfo)?.Name, property.Name))
            .Where(pair => pair.Member is not null)
            .ToFrozenDictionary(pair => pair.Member!, pair => pair.Name, StringComparer.Ordinal);

    private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;
}
