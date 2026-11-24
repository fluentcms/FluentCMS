using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Shell;

namespace FluentCMS.Admin.Administration.Roles;

// The catalogue of GET /permissions as the editor offers it: grouped by its source, each plugin permission with the plugin
// that contributes it (GET /plugins), and whether the caller holds what a grant names where the role applies (GET /me/access).
// The server decides: a role that grants more than the caller holds is refused with FCMS3103, and the editor only marks
// beforehand what the caller's access does not show it holds.
public sealed class GrantCatalogue
{
    public const string Core = "core";
    public const string Content = "content";
    public const string Plugin = "plugin";
    public const string Settings = "settings";

    // Every permission, also those of plugins and types added later.
    public const string Everything = "*";

    private const string Wildcard = "*";
    private const string ContentPrefix = "content.";

    private readonly AdminContext _context;
    private readonly Dictionary<string, PermissionRecord> _byName;
    private readonly Dictionary<string, string> _plugins;

    internal GrantCatalogue(IReadOnlyList<PermissionRecord> entries, AdminContext context)
    {
        Entries = entries;
        _context = context;
        _byName = entries.GroupBy(entry => entry.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _plugins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plugin in context.Plugins)
        {
            foreach (var permission in plugin.Contributions.Permissions)
            {
                _plugins.TryAdd(permission, plugin.Name);
            }
        }
    }

    public IReadOnlyList<PermissionRecord> Entries { get; }

    // The rules of GET /permissions/row-rules, or null when they could not be read.
    public IReadOnlyList<RowRuleRecord>? RowRules { get; init; }

    // The names of the media folders of the shell's site by id, read once a role has a media grant limited to resources.
    public IReadOnlyDictionary<string, string>? Folders { get; set; }

    // A grant of a media permission or pattern limited to resources, which are folder ids (decision record 0004).
    public static bool NamesFolders(string permission, JsonObject? constraints) =>
        permission.StartsWith("media.", StringComparison.Ordinal) && constraints?["resources"] is JsonArray { Count: > 0 };

    // The content type a grant names, as content.{type}.{operation} or content.{type}.*; null for any other grant.
    public static string? ContentTypeOf(string permission) =>
        ContentParts(permission) is var (type, _) && type != Wildcard ? type : null;

    public static bool IsContent(string permission) => ContentParts(permission) is not null;

    // The rules a content grant may name: those registered for every type, and those of its type when it names one.
    public IReadOnlyList<string> RulesFor(string permission)
    {
        var type = ContentTypeOf(permission);
        return [.. (RowRules ?? []).Where(rule => rule.TypeNames is null || (type is not null && rule.TypeNames.Contains(type, StringComparer.Ordinal))).Select(rule => rule.Name)];
    }

    // The sources in the order the editor offers them, core's always for Everything; one the admin does not know comes last.
    public IReadOnlyList<string> Sources =>
        [Core, .. Entries.Select(entry => entry.Source).Where(source => source != Core).Distinct(StringComparer.Ordinal).OrderBy(Order)];

    // What the editor offers for a source: Everything with core's, a pattern per operation over every content type and one
    // per type over every operation with the content permissions, then the source's permissions.
    public IReadOnlyList<string> Offered(string source)
    {
        IEnumerable<string> names = Entries.Where(entry => entry.Source == source).Select(entry => entry.Name);
        if (source == Core)
        {
            return [Everything, .. names];
        }

        if (source == Content)
        {
            var content = names.Select(ContentParts).OfType<(string Type, string Operation)>().ToList();
            return
            [
                .. content.Select(part => part.Operation).Distinct(StringComparer.Ordinal).Select(operation => $"{ContentPrefix}{Wildcard}.{operation}"),
                .. content.Select(part => part.Type).Distinct(StringComparer.Ordinal).Select(type => $"{ContentPrefix}{type}.{Wildcard}"),
                .. names,
            ];
        }

        return [.. names];
    }

    public PermissionRecord? Entry(string permission) => _byName.GetValueOrDefault(permission);

    // The plugin that contributes a permission, or null for core's, a pattern and a name no plugin lists.
    public string? PluginOf(string permission) => _plugins.GetValueOrDefault(permission);

    // The permissions of the catalogue a grant names, as the server matches a pattern: * for every one, or a name with * in
    // place of whole segments.
    public IReadOnlyList<PermissionRecord> Matches(string grant) =>
        grant == Everything ? Entries : [.. Entries.Where(entry => Matches(grant, entry.Name))];

    // Whether the caller's access shows that it holds what the grant names, with no limit it shows, in the role's site, or
    // for the instance for a role of every site. A Super Admin holds every permission. Of the content permissions, the
    // access shows those of the role's site when it is the shell's, and not restore and purge; for the rest the editor
    // cannot tell, and marks the grant. A mark never stops a save.
    public bool Holds(string grant, Guid? roleSite)
    {
        if (_context.User?.Roles.Any(role => role.Id == RoleScope.SuperAdmin && role.SiteId is null) == true)
        {
            return true;
        }

        // Everything also names what is added later, which only a Super Admin holds.
        if (grant == Everything || _context.AccessInfo is not { } access)
        {
            return false;
        }

        var matched = Matches(grant);
        return matched.Count > 0 && matched.All(entry => Holds(access, entry, roleSite));
    }

    private static int Order(string source) => source switch
    {
        Content => 1,
        Plugin => 2,
        Settings => 3,
        _ => 4,
    };

    private bool Holds(MyAccessInfo access, PermissionRecord entry, Guid? roleSite)
    {
        var inShellSite = roleSite is { } site && site == _context.Site?.Id;
        if (entry.Source == Content)
        {
            return inShellSite && ContentParts(entry.Name) is var (type, operation)
                && access.Types.TryGetValue(type, out var may)
                && !may.Conditional.Contains(operation, StringComparer.Ordinal)
                && operation switch
                {
                    "read" => true,
                    "create" => may.Create && !LimitedToFields(type, may.CreatableFields),
                    "update" => may.Update && !LimitedToFields(type, may.UpdatableFields),
                    "delete" => may.Delete,
                    "publish" => may.Publish,
                    "unpublish" => may.Unpublish,
                    _ => false,
                };
        }

        return access.InstancePermissions.Contains(entry.Name, StringComparer.Ordinal)
            || (inShellSite && access.Permissions.Contains(entry.Name, StringComparer.Ordinal));
    }

    // Whether the caller creates or changes only some of the fields the schema shows of the type, as the access lists them
    // (Conditional names row rules and locales only). The server checks each grant on its own, so a limit of create or
    // update says nothing of the other operations, and only a grant of create or update is marked for it. A type the schema
    // does not show counts as limited, which happens without the schema only (no schema.read in the site, or /schema
    // answered 403), since a type the caller may not read is missing from the access too and is marked already; then every
    // grant of create or update is marked. The client cannot see a field limit on read, delete, publish or unpublish, nor a
    // limit of create or update to the very fields the caller reads, since /schema and /me/access both show the readable
    // fields only: content.article.*{fields:[title]} shows article as {title} with nothing limited. Such a grant is not
    // marked, and the server refuses it with FCMS3103.
    private bool LimitedToFields(string type, IReadOnlyList<string> writable)
    {
        if (_context.Schema?.Types.FirstOrDefault(item => item.Name == type) is not { } described)
        {
            return true;
        }

        return !described.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal).IsSubsetOf(writable);
    }

    // content.{type}.{operation}, or null for any other name.
    private static (string Type, string Operation)? ContentParts(string name)
    {
        var segments = name.Split('.');
        return segments is [ "content", var type, var operation ] ? (type, operation) : null;
    }

    private static bool Matches(string grant, string permission)
    {
        var pattern = grant.Split('.');
        var segments = permission.Split('.');
        return pattern.Length == segments.Length
            && pattern.Zip(segments).All(pair => pair.First == Wildcard || string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
    }
}
