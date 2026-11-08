using FluentCMS.Content;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.Content;

// The plugins' resolvers of reference targets that are no content type, by target. A target named like a content type is
// that type's, which no resolver takes.
internal sealed class ReferenceResolvers(IEnumerable<IReferenceResolver> resolvers, SchemaModel schema)
{
    private readonly Dictionary<string, IReferenceResolver> _byTarget = ByTarget(resolvers);

    public bool IsEmpty => _byTarget.Count == 0;

    public IReferenceResolver? Find(string target) =>
        schema.FindContentType(target) is null ? _byTarget.GetValueOrDefault(target) : null;

    // A write refuses only the targets it adds, so an entry whose asset was deleted since can still be saved.
    public async Task CheckAddedAsync(EntryStore store, SchemaType type, ContentDocument entry, ContentDocument? stored, ValidationContext result, CancellationToken cancellationToken)
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
            foreach (var (path, target, id) in group.Where(found => missing.Contains(found.Id)).Distinct())
            {
                result.AddError(path, ValidationErrorCodes.UnknownReference, $"No {target} {id:D} exists in this site.");
            }
        }
    }

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
