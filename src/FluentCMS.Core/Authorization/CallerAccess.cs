using FluentCMS.Authorization;
using FluentCMS.Content;

namespace FluentCMS.Core.Authorization;

internal sealed record CallerAccessResponse(
    Guid Site,
    string Locale,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> InstancePermissions,
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
// describes, as the MCP tool list reads it, so it follows the evaluator that the content service asks; the permissions are
// those of the catalogue that are not per type, held as HeldAsync counts them, in the site and for the instance. It shows
// nothing beyond the caller's own reach: no other principal's grants, and only the types and fields it may read.
internal sealed class CallerAccess(IEffectiveAccess access, IAuthorizationEvaluator evaluator, IOperationContextAccessor accessor, PermissionCatalogue catalogue)
{
    private static readonly string[] Operations = ["read", "create", "update", "delete", "publish", "unpublish"];

    public async Task<CallerAccessResponse> GetAsync(CancellationToken cancellationToken)
    {
        var operation = accessor.Current ?? throw new InvalidOperationException(
            $"{AuthorizationErrorCodes.NoOperation}: access was described with no current operation.");

        List<string> names = [.. catalogue.Entries.Where(entry => entry.Source != PermissionSource.Content).Select(entry => entry.Name)];
        var content = await access.ContentAsync(cancellationToken);
        var site = await access.HeldAsync(names, cancellationToken);
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        var instance = new AccessTarget(null, null);

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
            [.. names.Where(site.Contains)],
            [.. names.Where(name => permissions.ResourcesOf(name, instance) is not { Count: 0 })],
            content.Search,
            types);
    }
}
