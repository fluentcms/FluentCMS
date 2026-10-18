using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;

namespace FluentCMS.Admin.Components.Pages;

// Diffs for the DiffView section of /ui, built by Core the way the desk gets them: an update, a create and a delete, and a set of
// 200 items to watch the progressive rendering (PERF-04).
internal static class ShowcaseDiffs
{
    public static ChangeSetDiff Sample { get; } = new(Guid.NewGuid(),
    [
        ItemDiff.ForSchema(
            Operation(OperationKind.Update, 3, 4),
            Definition("""
                {"entity":false,"fields":[
                  {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
                  {"name":"price","type":"number","required":false,"settings":{"min":0}},
                  {"name":"color","type":"text","required":false,"settings":{}}
                ]}
                """),
            Definition("""
                {"entity":true,"fields":[
                  {"name":"price","type":"number","required":true,"settings":{"min":0}},
                  {"name":"title","type":"text","required":true,"settings":{"maxLength":200}},
                  {"name":"stock","type":"number","required":false,"settings":{"min":0}}
                ]}
                """)),
        ItemDiff.ForSchema(
            Operation(OperationKind.Create, null, 1),
            null,
            Definition("""
                {"entity":false,"fields":[
                  {"name":"headline","type":"text","required":true,"settings":{"maxLength":120}},
                  {"name":"body","type":"text","required":false,"settings":{}}
                ]}
                """)),
        ItemDiff.ForSchema(
            Operation(OperationKind.Delete, 2, null),
            Definition("""
                {"entity":false,"fields":[
                  {"name":"image","type":"text","required":true,"settings":{}}
                ]}
                """),
            null),
    ]);

    public static ChangeSetDiff Large()
    {
        var items = Enumerable.Range(1, 200).Select(version => ItemDiff.ForSchema(
            Operation(OperationKind.Update, version, version + 1),
            Definition($$$"""{"entity":false,"fields":[{"name":"title","type":"text","required":true,"settings":{"maxLength":{{{version}}}}}]}"""),
            Definition($$$"""{"entity":false,"fields":[{"name":"title","type":"text","required":true,"settings":{"maxLength":{{{version + 1}}}}}]}""")));

        return new ChangeSetDiff(Guid.NewGuid(), items.ToList());
    }

    private static ChangeOperation Operation(OperationKind kind, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, ItemKind.Schema, Guid.NewGuid(), baseVersion, proposedVersion).Value;
    }

    private static ContentTypeDefinition Definition(string json)
    {
        return ContentTypeDefinition.Parse(json).Value;
    }
}
