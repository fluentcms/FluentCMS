using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Plugins;

namespace FluentCMS.Core.Authorization;

internal sealed record CallerAccessResponse(
    Guid Site,
    string Locale,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> InstancePermissions,
    IReadOnlyList<string> LimitedPermissions,
    bool Search,
    IReadOnlyDictionary<string, CallerTypeAccess> Types);

internal sealed record CallerTypeAccess(
    bool Drafts,
    bool Create,
    bool Update,
    bool Delete,
    bool Publish,
    bool Unpublish,
    IReadOnlyList<string> CreatableFields,
    IReadOnlyList<string> UpdatableFields,
    IReadOnlyList<string> Conditional);

// What the caller may do in the site of the request, for GET /me/access (spec 4.10). The content part is what IEffectiveAccess
// describes, as the MCP tool list reads it, so it follows the evaluator that the content service asks. The permissions are
// those of the catalogue that are not per type: held in the site or for the instance when a grant with no resource limit
// gives them, as ManagementAuthority asks, and limited when only grants limited to resources do, which a plugin narrows
// call by call. A plugin disabled in the site answers 404 there, so its permissions are left out of the site's lists. It
// shows nothing beyond the caller's own reach: no other principal's grants, and only the types and fields it may read.
internal sealed class CallerAccess(
    IEffectiveAccess access,
    IAuthorizationEvaluator evaluator,
    IOperationContextAccessor accessor,
    PermissionCatalogue catalogue,
    PluginRegistry plugins,
    PluginState state)
{
    private static readonly string[] Operations = [.. EffectiveAccess.Described.Select(CorePermissions.OperationName)];

    public async Task<CallerAccessResponse> GetAsync(CancellationToken cancellationToken)
    {
        var operation = accessor.Current ?? throw new InvalidOperationException(
            $"{AuthorizationErrorCodes.NoOperation}: access was described with no current operation.");

        List<string> names = [.. catalogue.Entries.Where(entry => entry.Source != PermissionSource.Content).Select(entry => entry.Name)];
        var content = await access.ContentAsync(cancellationToken);
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        var disabled = await state.DisabledAsync(operation.Site.Value, cancellationToken);
        var site = new AccessTarget(operation.Site, null);
        var instance = new AccessTarget(null, null);
        List<string> inSite = [.. names.Where(name => disabled.Count == 0 || catalogue.ProviderOf(name) is not { } provider || plugins.OwnerOf(provider) is not { } plugin || !disabled.Contains(plugin.Name))];

        Dictionary<string, CallerTypeAccess> types = new(StringComparer.Ordinal);
        foreach (var type in content.Schema.ContentTypes)
        {
            var may = content.Types[type.Name];
            List<string> fields = [.. type.Fields.Select(field => field.Name)];
            types[type.Name] = new CallerTypeAccess(
                may.Drafts,
                may.Create,
                may.Update,
                may.Delete,
                may.Publish,
                may.Unpublish,
                [.. fields.Where(may.CreatableFields.Contains)],
                [.. fields.Where(may.UpdatableFields.Contains)],
                [.. Operations.Where(may.Conditional.Contains)]);
        }

        return new CallerAccessResponse(
            operation.Site.Value,
            operation.Locale,
            [.. inSite.Where(name => permissions.Allows(name, site))],
            [.. names.Where(name => permissions.Allows(name, instance))],
            [.. inSite.Where(name => !permissions.Allows(name, site) && permissions.ResourcesOf(name, site) is { Count: > 0 })],
            content.Search,
            types);
    }
}
