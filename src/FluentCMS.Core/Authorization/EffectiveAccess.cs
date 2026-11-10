using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Providers;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// What the principal of the operation may do, for a surface that lists only what a call would allow (spec 4.8, 5.4). It
// asks the evaluator and enforces nothing. A type is one the principal may read in the site and locale of the operation,
// narrowed as the schema group narrows it, and an operation or a field counts when some grant gives it there, a grant
// with a row rule included, since the entry it holds for is not known here: the service decides each call.
internal sealed class EffectiveAccess(
    SchemaModel schema,
    IAuthorizationEvaluator evaluator,
    IOperationContextAccessor accessor,
    ISearchIndex search) : IEffectiveAccess
{
    public async ValueTask<ContentAccess> ContentAsync(CancellationToken cancellationToken = default)
    {
        var operation = Operation();
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        var target = new AccessTarget(operation.Site, operation.Locale);
        var visible = permissions.IsSystem ? schema : SchemaIntrospection.Narrow(schema, permissions, target);

        Dictionary<string, ContentTypeAccess> types = new(StringComparer.Ordinal);
        foreach (var shown in visible.ContentTypes)
        {
            // The grants are asked of the type as the schema has it, whose scope decides which of them write it.
            var type = schema.FindContentType(shown.Name)!;
            types[shown.Name] = new ContentTypeAccess
            {
                Drafts = permissions.CanReadDraft(type, target),
                Create = permissions.Allows(type, ContentOperation.Create, target),
                Update = permissions.Allows(type, ContentOperation.Update, target),
                Delete = permissions.Allows(type, ContentOperation.Delete, target),
                Publish = permissions.Allows(type, ContentOperation.Publish, target),
                Unpublish = permissions.Allows(type, ContentOperation.Unpublish, target),
                CreatableFields = Writable(permissions, type, shown, ContentOperation.Create, target),
                UpdatableFields = Writable(permissions, type, shown, ContentOperation.Update, target),
            };
        }

        return new ContentAccess { Schema = visible, Types = types, Search = search is not NoSearchIndex };
    }

    public async ValueTask<IReadOnlySet<string>> HeldAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var operation = Operation();
        var effective = await evaluator.ResolveAsync(operation, cancellationToken);
        var target = new AccessTarget(operation.Site, null);
        return permissions
            .Where(permission => effective.ResourcesOf(permission, target) is not { Count: 0 })
            .ToHashSet(StringComparer.Ordinal);
    }

    // The readable fields that a grant behind the operation lets through: all of them when one has no field list.
    private static HashSet<string> Writable(EffectivePermissions permissions, SchemaType type, SchemaType shown, ContentOperation operation, AccessTarget target)
    {
        var readable = shown.Fields.Select(field => field.Name);
        if (permissions.IsSystem)
        {
            return readable.ToHashSet(StringComparer.Ordinal);
        }

        var grants = permissions.GrantsOf(type, operation, target);
        if (grants.Any(grant => grant.Constraints.Fields is null))
        {
            return readable.ToHashSet(StringComparer.Ordinal);
        }

        var listed = grants.SelectMany(grant => grant.Constraints.Fields!).ToHashSet(StringComparer.Ordinal);
        return readable.Where(listed.Contains).ToHashSet(StringComparer.Ordinal);
    }

    // Thrown rather than answered empty, as ManagementAuthority does: code that forgot its operation fails where it runs.
    private OperationContext Operation() => accessor.Current ?? throw new InvalidOperationException(
        $"{AuthorizationErrorCodes.NoOperation}: access was described with no current operation. Host code, a job or a seed that acts as the system runs inside SystemOperation.Begin.");
}
