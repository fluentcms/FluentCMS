using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// The seam for the authorization evaluator (#2701). A client cannot filter or sort by what it cannot read, or a
// filter would reveal the value. A row rule may name such a field: the engine compiles it without these checks.
internal interface IContentQueryAccess
{
    bool CanReadType(SchemaType type);

    // The field is a top-level field of the type or a system field such as createdBy.
    bool CanReadField(SchemaType type, string field);

    // Joins the client's filter in an And node before compilation, on the queried type and on every relation target.
    ContentFilter? RowRule(SchemaType type);
}

internal sealed class OpenContentQueryAccess : IContentQueryAccess
{
    public bool CanReadType(SchemaType type) => true;

    public bool CanReadField(SchemaType type, string field) => true;

    public ContentFilter? RowRule(SchemaType type) => null;
}
