using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Content;

// What a scope resolved leaves its cache tags, for a Delivery read to put on its copy; a tag the cache tag header cannot
// carry is left out.
public class ReferenceResolversTests
{
    [Fact]
    public async Task TheCacheTagsOfWhatAScopeResolvedAreKeptOnceAndOnlyThoseAHeaderCanCarry()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var resolver = new TaggingResolver(new Dictionary<Guid, string[]>
        {
            [first] = ["media:first", "media-presets:site", "two words", "a,b", "", "café"],
            [second] = ["media:second", "media-presets:site"],
        });
        var resolvers = new ReferenceResolvers([resolver], new SchemaBuilder().Build());

        resolvers.CacheTags.ShouldBeEmpty();
        await resolvers.ResolveAsync(resolver, [first], "en", TestContext.Current.CancellationToken);
        await resolvers.ResolveAsync(resolver, [second, Guid.CreateVersion7()], "en", TestContext.Current.CancellationToken);

        resolvers.CacheTags.ShouldBe(["media:first", "media-presets:site", "media:second"], ignoreOrder: true);
    }

    private sealed class TaggingResolver(Dictionary<Guid, string[]> tags) : IReferenceResolver
    {
        public string TargetType => "media";

        public Task<IReadOnlyDictionary<Guid, ResolvedReference>> ResolveAsync(IReadOnlyCollection<Guid> ids, string locale, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ResolvedReference>>(ids
                .Where(tags.ContainsKey)
                .ToDictionary(id => id, id => new ResolvedReference(new ContentDocument { ["id"] = id, ["type"] = TargetType }) { CacheTags = tags[id] }));

        public Task<IReadOnlySet<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }
}
