using FluentCMS.Content;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Content;

// The plugins' resolvers of reference targets that are no content type, by target. A target named like a content type is
// that type's, which no resolver takes. The cache tags of what a scope resolved are kept, for a Delivery read to put on its
// copy beside its own.
internal sealed class ReferenceResolvers(IEnumerable<IReferenceResolver> resolvers, SchemaModel schema)
{
    private readonly Dictionary<string, IReferenceResolver> _byTarget = ByTarget(resolvers);

    private readonly HashSet<string> _cacheTags = new(StringComparer.Ordinal);

    public bool IsEmpty => _byTarget.Count == 0;

    public IReadOnlyCollection<string> CacheTags
    {
        get
        {
            lock (_cacheTags)
            {
                return [.. _cacheTags];
            }
        }
    }

    public IReferenceResolver? Find(string target) =>
        schema.FindContentType(target) is null ? _byTarget.GetValueOrDefault(target) : null;

    public async Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReferenceResolver resolver, IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveAsync(ids, locale, cancellationToken);
        lock (_cacheTags)
        {
            _cacheTags.UnionWith(resolved.Values.SelectMany(reference => reference.CacheTags).Where(IsTag));
        }

        return resolved;
    }

    // A write refuses only the targets it adds, so an entry whose asset was deleted since can still be saved.
    // provided holds the targets that a dry run of an import counts as there, because the package brings them.
    public async Task CheckAddedAsync(EntryStore store, SchemaType type, ContentDocument entry, ContentDocument? stored, ValidationContext result, CancellationToken cancellationToken, IReadOnlySet<(string Target, Guid Id)>? provided = null)
    {
        if (IsEmpty)
        {
            return;
        }

        HashSet<(string Target, Guid Id)> before = stored is null ? [] : [.. store.OtherTargetsOf(type, stored).Select(found => (found.Target, found.Id))];
        var added = store.OtherTargetsOf(type, entry).Where(found => !before.Contains((found.Target, found.Id))).ToList();
        foreach (var group in added.GroupBy(found => found.Target, StringComparer.Ordinal))
        {
            if (Find(group.Key) is not { } resolver)
            {
                continue;
            }

            var missing = await resolver.FindMissingAsync([.. group.Select(found => found.Id).Distinct()], cancellationToken);
            if (provided is { Count: > 0 })
            {
                missing = missing.Where(id => !provided.Contains((group.Key, id))).ToHashSet();
            }

            foreach (var (path, target, id) in group.Where(found => missing.Contains(found.Id)).Distinct())
            {
                result.AddError(path, ValidationErrorCodes.UnknownReference, $"No {target} {id:D} exists in this site.");
            }
        }
    }

    // Visible ASCII without the separators of the cache tag header.
    private static bool IsTag(string? tag) => !string.IsNullOrEmpty(tag) && tag.All(character => character is > ' ' and <= '~' and not ',');

    private static Dictionary<string, IReferenceResolver> ByTarget(IEnumerable<IReferenceResolver> resolvers)
    {
        Dictionary<string, IReferenceResolver> byTarget = new(StringComparer.Ordinal);
        foreach (var resolver in resolvers)
        {
            if (!byTarget.TryAdd(resolver.TargetType, resolver))
            {
                throw new InvalidOperationException($"{resolver.GetType().FullName} and {byTarget[resolver.TargetType].GetType().FullName} both resolve '{resolver.TargetType}'; a target has one resolver.");
            }
        }

        return byTarget;
    }
}

// Builds the resolvers once at start, so that two of one target stop the host instead of failing every read and write.
internal sealed class ReferenceResolversStartupCheck(IServiceScopeFactory scopes) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ReferenceResolvers>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
