using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Content;

// The field actions a form offers on each field of its type (spec 5.1): those of the enabled halves whose permission the user
// holds, for the field's type, and for an action that names FieldsFrom only on the fields its answer names. An answer is read
// once per site and locale in the session and kept; a read that fails, or a FieldsFrom the client would refuse, offers its
// actions on no field. No half is loaded here.
internal sealed class FieldActionOffers(AdminPlugins plugins, IManagementApi api, ILogger<FieldActionOffers> logger)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> None = new Dictionary<string, IReadOnlyList<PluginFieldAction>>();

    private readonly Dictionary<(string? Site, string? Locale, string Path), JsonNode?> _answers = [];

    // What the form offers before its reads answer: the actions without FieldsFrom, and those whose answer is kept.
    public IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> Known(SchemaTypeDescription type, string? site, string? locale) =>
        Offered(type, site, locale, Declared(type));

    // Whether a FieldsFrom of the type's actions is not read yet in the site and locale.
    public bool Reads(SchemaTypeDescription type, string? site, string? locale) =>
        Paths(Declared(type)).Any(path => !_answers.ContainsKey((site, locale, path)));

    // Reads what is not kept yet, each path once, and answers the actions of every field. A cancellation is the caller's.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>>> ReadAsync(SchemaTypeDescription type, string? site, string? locale, CancellationToken cancellationToken)
    {
        var declared = Declared(type);
        foreach (var path in Paths(declared).Where(path => !_answers.ContainsKey((site, locale, path))))
        {
            var request = new ApiRequest(HttpMethod.Get, path) { Site = site, NoSite = site is null, Locale = locale, NoLocale = locale is null };
            try
            {
                _answers[(site, locale, path)] = (await api.SendAsync(request, ManagementJsonContext.Default.JsonNode, cancellationToken)).Value;
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

        return Offered(type, site, locale, declared);
    }

    private Dictionary<string, IReadOnlyList<PluginFieldAction>> Declared(SchemaTypeDescription type) =>
        type.Fields.ToDictionary(field => field.Name, field => plugins.FieldActions(field.Type), StringComparer.Ordinal);

    private static IEnumerable<string> Paths(Dictionary<string, IReadOnlyList<PluginFieldAction>> declared) =>
        declared.Values.SelectMany(actions => actions).Select(Source).OfType<(string Path, string Member)>().Select(source => source.Path).Distinct(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, IReadOnlyList<PluginFieldAction>> Offered(SchemaTypeDescription type, string? site, string? locale, Dictionary<string, IReadOnlyList<PluginFieldAction>> declared)
    {
        var offered = declared
            .Select(pair => (Field: pair.Key, Actions: (IReadOnlyList<PluginFieldAction>)[.. pair.Value.Where(action => Offers(action, type.Name, pair.Key, site, locale))]))
            .Where(pair => pair.Actions.Count > 0)
            .ToDictionary(pair => pair.Field, pair => pair.Actions, StringComparer.Ordinal);
        return offered.Count == 0 ? None : offered;
    }

    private static (string Path, string Member)? Source(PluginFieldAction action) =>
        action.Declared.FieldsFrom is { } from && Split(from) is { } source ? source : null;

    // A FieldsFrom that does not read offers the action on no field, as a read that fails, or one not answered yet.
    private bool Offers(PluginFieldAction action, string type, string field, string? site, string? locale)
    {
        if (action.Declared.FieldsFrom is null)
        {
            return true;
        }

        if (Source(action) is not { } source || _answers.GetValueOrDefault((site, locale, source.Path)) is not JsonObject answer)
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

    // As FieldActionSource of the contract, which the manifest check holds a half to: a path the client takes, with no dot
    // segment, then '#' and a member.
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

        return (path, value[(hash + 1)..]);
    }
}
