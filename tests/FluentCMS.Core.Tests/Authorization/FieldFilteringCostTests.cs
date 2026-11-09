using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// Field filtering asks about every key of every entry of a list, so after the first entry the answers must come without
// going through the grants again, however many grants the principal has.
public class FieldFilteringCostTests
{
    private const int FieldCount = 40;

    private static readonly SiteId Site = new(Guid.CreateVersion7());

    private static readonly SchemaModel Model = new SchemaBuilder().AddCollection("article", type =>
    {
        for (var index = 0; index < FieldCount; index++)
        {
            type.Field(Field(index), "text");
        }
    }).Build();

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    public void AfterTheFirstEntryFilteringAListDoesNoMoreWorkForMoreGrants(int count)
    {
        var grants = new CountingGrants(Grants(count));
        var access = new FieldAccess(Permissions(grants), Model);
        var type = Model.FindContentType("article")!;
        var entries = Entries(100);

        access.OmitUnreadable(entries[0], type, Target);
        var first = grants.Reads;
        foreach (var entry in entries.Skip(1))
        {
            access.OmitUnreadable(entry, type, Target);
        }

        first.ShouldBeGreaterThanOrEqualTo(count);
        grants.Reads.ShouldBe(first);
    }

    [Fact]
    public void TheAnswersAreTheSameForEveryEntry()
    {
        var access = new FieldAccess(Permissions(Grants(20)), Model);
        var type = Model.FindContentType("article")!;

        foreach (var entry in Entries(3))
        {
            access.OmitUnreadable(entry, type, Target);

            entry.Keys.Order(StringComparer.Ordinal).ShouldBe([.. Enumerable.Range(0, 30).Select(Field), "id", "locale", "status"]);
        }
    }

    private static AccessTarget Target => new(Site, "en");

    private static List<EffectiveGrant> Grants(int grants)
    {
        List<EffectiveGrant> all = [];
        for (var index = 0; index < grants - 2; index++)
        {
            all.Add(new EffectiveGrant($"content.other{index:000}.read", PermissionConstraints.None, null));
        }

        all.Add(new EffectiveGrant("content.article.read", new PermissionConstraints { Fields = [.. Enumerable.Range(0, 30).Select(Field)] }, null));
        all.Add(new EffectiveGrant("content.*.read", new PermissionConstraints { Fields = [.. Enumerable.Range(10, 30).Select(Field)], RowRule = "own-entries" }, null));
        return all;
    }

    private static EffectivePermissions Permissions(IReadOnlyList<EffectiveGrant> grants)
    {
        var operation = new OperationContext(Principal.Anonymous, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow);
        return new EffectivePermissions(operation, grants, new RowRuleRegistry(Model, new ContentQueryOptions(), []));
    }

    private static List<ContentDocument> Entries(int count)
    {
        List<ContentDocument> entries = [];
        for (var entry = 0; entry < count; entry++)
        {
            var document = new ContentDocument { ["id"] = Guid.CreateVersion7(), ["status"] = "published", ["locale"] = "en" };
            for (var index = 0; index < FieldCount; index++)
            {
                document[Field(index)] = "value";
            }

            entries.Add(document);
        }

        return entries;
    }

    private static string Field(int index) => $"f{index:00}";

    // Counts every grant read, by index or through an enumerator.
    private sealed class CountingGrants(List<EffectiveGrant> grants) : IReadOnlyList<EffectiveGrant>
    {
        public int Reads { get; private set; }

        public int Count => grants.Count;

        public EffectiveGrant this[int index]
        {
            get
            {
                Reads++;
                return grants[index];
            }
        }

        public IEnumerator<EffectiveGrant> GetEnumerator()
        {
            foreach (var grant in grants)
            {
                Reads++;
                yield return grant;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
