using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Content;

// The field actions a form offers on each field of its type (spec 5.1): those of the enabled halves whose permission the user
// holds, for the field's type, and for an action that names FieldsFrom only on the fields its answer names. Each read is made
// once, in the form's site and locale; one that fails offers its actions on no field. No half is loaded here.
internal static class FieldActionOffers
{
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>>> ReadAsync(
        AdminPlugins plugins,
        IManagementApi api,
        SchemaTypeDescription type,
        string? site,
        string? locale,
        ILogger logger)
    {
        var declared = type.Fields.ToDictionary(field => field.Name, field => plugins.FieldActions(field.Type), StringComparer.Ordinal);
        Dictionary<string, JsonNode?> answers = new(StringComparer.Ordinal);
        foreach (var path in declared.Values.SelectMany(actions => actions).Select(Source).OfType<(string Path, string Member)>().Select(source => source.Path).Distinct(StringComparer.Ordinal))
        {
            var request = new ApiRequest(HttpMethod.Get, path) { Site = site, NoSite = site is null, Locale = locale, NoLocale = locale is null };
            try
            {
                answers[path] = (await api.SendAsync(request, ManagementJsonContext.Default.JsonNode, CancellationToken.None)).Value;
            }
            catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                PluginLog.FieldsNotRead(logger, path, exception);
                answers[path] = null;
            }
        }

        return declared
            .Select(pair => (Field: pair.Key, Actions: (IReadOnlyList<PluginFieldAction>)[.. pair.Value.Where(action => Offers(action, type.Name, pair.Key, answers))]))
            .Where(pair => pair.Actions.Count > 0)
            .ToDictionary(pair => pair.Field, pair => pair.Actions, StringComparer.Ordinal);
    }

    private static (string Path, string Member)? Source(PluginFieldAction action) =>
        action.Declared.FieldsFrom is { } from && Split(from) is { } source ? source : null;

    // A FieldsFrom that does not read offers the action on no field, as a read that fails.
    private static bool Offers(PluginFieldAction action, string type, string field, Dictionary<string, JsonNode?> answers)
    {
        if (action.Declared.FieldsFrom is null)
        {
            return true;
        }

        if (Source(action) is not { } source || answers.GetValueOrDefault(source.Path) is not JsonObject answer)
        {
            return false;
        }

        return answer[source.Member] switch
        {
            JsonValue all when all.TryGetValue<bool>(out var every) => every,
            JsonObject types => types[type] is JsonArray names && names.Any(name => name is JsonValue value && value.TryGetValue<string>(out var text) && text == field),
            _ => false,
        };
    }

    // As FieldActionSource of the contract, which the manifest check holds a half to: a path the client takes, '#', a member.
    private static (string Path, string Member)? Split(string value)
    {
        var hash = value.LastIndexOf('#');
        if (hash <= 0 || hash == value.Length - 1)
        {
            return null;
        }

        try
        {
            _ = new ApiRequest(HttpMethod.Get, value[..hash]);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return (value[..hash], value[(hash + 1)..]);
    }
}
