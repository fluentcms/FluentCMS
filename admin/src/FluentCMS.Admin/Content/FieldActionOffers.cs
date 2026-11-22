using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Content;

// The field actions a form offers on each field of its type (spec 5.1): those of the enabled halves whose permission the user
// holds, for the field's type, and for an action that names FieldsFrom only on the fields its answer names. The form reads
// each FieldsFrom once each time it loads, for all its fields, and keeps nothing: the site's settings or the caller's grants
// may have changed since. A read that fails, or a FieldsFrom the client would refuse, offers its actions on no field. No half
// is loaded here.
internal sealed class FieldActionOffers(AdminPlugins plugins, IManagementApi api, ILogger<FieldActionOffers> logger)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> None = new Dictionary<string, IReadOnlyList<PluginFieldAction>>();

    // The client's root, against which a FieldsFrom is resolved as the client resolves the path it sends.
    private static readonly Uri Root = new("https://admin.invalid/api/management/v1/");

    // What the form offers before its reads answer: the actions without FieldsFrom.
    public IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> Known(SchemaTypeDescription type) =>
        Offered(type, Declared(type), new Dictionary<string, JsonNode?>());

    // Whether the type's actions name a FieldsFrom to read.
    public bool Reads(SchemaTypeDescription type) => Paths(Declared(type)).Any();

    // Reads each FieldsFrom of the type's actions once, in the site and locale, and answers the actions of every field. A
    // cancellation is the caller's.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>>> ReadAsync(SchemaTypeDescription type, string? site, string? locale, CancellationToken cancellationToken)
    {
        var declared = Declared(type);
        var answers = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var path in Paths(declared))
        {
            var request = new ApiRequest(HttpMethod.Get, path) { Site = site, NoSite = site is null, Locale = locale, NoLocale = locale is null };
            try
            {
                answers[path] = (await api.SendAsync(request, ManagementJsonContext.Default.JsonNode, cancellationToken)).Value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Whatever the read throws, the form goes on without the actions it gates.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                PluginLog.FieldsNotRead(logger, path, exception);
            }
        }

        return Offered(type, declared, answers);
    }

    private Dictionary<string, IReadOnlyList<PluginFieldAction>> Declared(SchemaTypeDescription type) =>
        type.Fields.ToDictionary(field => field.Name, field => plugins.FieldActions(field.Type), StringComparer.Ordinal);

    private static IEnumerable<string> Paths(Dictionary<string, IReadOnlyList<PluginFieldAction>> declared) =>
        declared.Values.SelectMany(actions => actions).Select(Source).OfType<(string Path, string Member)>().Select(source => source.Path).Distinct(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> Offered(SchemaTypeDescription type, Dictionary<string, IReadOnlyList<PluginFieldAction>> declared, Dictionary<string, JsonNode?> answers)
    {
        var offered = declared
            .Select(pair => (Field: pair.Key, Actions: (IReadOnlyList<PluginFieldAction>)[.. pair.Value.Where(action => Offers(action, type.Name, pair.Key, answers))]))
            .Where(pair => pair.Actions.Count > 0)
            .ToDictionary(pair => pair.Field, pair => pair.Actions, StringComparer.Ordinal);
        return offered.Count == 0 ? None : offered;
    }

    private static (string Path, string Member)? Source(PluginFieldAction action) =>
        action.Declared.FieldsFrom is { } from && Split(from) is { } source ? source : null;

    // A FieldsFrom that does not read offers the action on no field, as a read that fails, or one not answered yet.
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

    // As FieldActionSource of the contract, which the manifest check holds a half to: a path the client takes and resolves
    // under its root, with no dot segment, then '#' and a member.
    private static (string Path, string Member)? Split(string value)
    {
        var hash = value.LastIndexOf('#');
        if (hash <= 0 || hash == value.Length - 1)
        {
            return null;
        }

        var path = value[..hash];
        if (path.Split('?', 2)[0].Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or ".."))
        {
            return null;
        }

        try
        {
            _ = new ApiRequest(HttpMethod.Get, path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!Uri.TryCreate(Root, path, out var target) || !Root.IsBaseOf(target)
            || !string.Equals(target.GetLeftPart(UriPartial.Authority), Root.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (path, value[(hash + 1)..]);
    }
}
