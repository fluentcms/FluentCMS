using FluentCMS.Content;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content.Query;

// What the engine asks about a principal. A client cannot filter or sort by what it cannot read, or a filter would
// reveal the value. A row rule may name such a field: the engine compiles it without these checks.
internal interface IContentQueryAccess
{
    // The answers for one query, which may read only some sites and locales of a grant. These methods are synchronous
    // because compiling is, so loading what they need happens here. The engine runs a query on what this returns.
    ValueTask<IContentQueryAccess> ForQueryAsync(SiteId? site, string locale, CancellationToken cancellationToken) => ValueTask.FromResult(this);

    bool CanReadType(SchemaType type);

    // Reading draft rows needs a preview key or the draft scope, on the queried type and on every relation target.
    bool CanReadDraft(SchemaType type);

    // The field is a top-level field of the type or a system field such as createdBy.
    bool CanReadField(SchemaType type, string field);

    // Joins the client's filter in an And node before compilation, on the queried type and on every relation target.
    ContentFilter? RowRule(SchemaType type);
}
