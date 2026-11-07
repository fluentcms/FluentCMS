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
// going through the grants again: what it allocates then does not depend on how many grants the principal has.
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

    [Fact]
    public void AfterTheFirstEntryFilteringAListDoesNoMoreWorkForMoreGrants()
    {
        var few = AllocatedAfterTheFirstEntry(Permissions(20));
        var many = AllocatedAfterTheFirstEntry(Permissions(200));

        many.ShouldBeLessThanOrEqualTo(few + 1024);
    }

    [Fact]
    public void TheAnswersAreTheSameForEveryEntry()
    {
        var access = new FieldAccess(Permissions(20), Model);
        var type = Model.FindContentType("article")!;

        foreach (var entry in Entries(3))
        {
            access.OmitUnreadable(entry, type, Target);

            entry.Keys.Order(StringComparer.Ordinal).ShouldBe([.. Enumerable.Range(0, 30).Select(Field), "id", "locale", "status"]);
        }
    }

    private static AccessTarget Target => new(Site, "en");

    private static EffectivePermissions Permissions(int grants)
    {
        List<EffectiveGrant> all = [];
        for (var index = 0; index < grants - 2; index++)
        {
            all.Add(new EffectiveGrant($"content.other{index:000}.read", PermissionConstraints.None, null));
        }

        all.Add(new EffectiveGrant("content.article.read", new PermissionConstraints { Fields = [.. Enumerable.Range(0, 30).Select(Field)] }, null));
        all.Add(new EffectiveGrant("content.*.read", new PermissionConstraints { Fields = [.. Enumerable.Range(10, 30).Select(Field)], RowRule = "own-entries" }, null));

        var operation = new OperationContext(Principal.Anonymous, Origin.Api, Site, "en", null, "test", DateTimeOffset.UtcNow);
        return new EffectivePermissions(operation, all, new RowRuleRegistry(Model, new ContentQueryOptions(), []));
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

    private static long AllocatedAfterTheFirstEntry(EffectivePermissions permissions)
    {
        var access = new FieldAccess(permissions, Model);
        var type = Model.FindContentType("article")!;
        var entries = Entries(100);
        access.OmitUnreadable(entries[0], type, Target);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 1; index < entries.Count; index++)
        {
            access.OmitUnreadable(entries[index], type, Target);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Field(int index) => $"f{index:00}";
}
