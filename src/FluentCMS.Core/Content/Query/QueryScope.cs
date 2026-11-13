using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content.Query;

// What one compilation shares: the state, locale and site every joined row must have, and the errors found so far.
internal sealed class QueryScope(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options, short state, string locale, SiteId? site, bool reachesTrash = false, bool management = false)
{
    private readonly List<ValidationError> _errors = [];
    private int _populated;

    public CmsDbContext Context => context;

    public SchemaModel Schema => schema;

    public IJsonSqlDialect Dialect => dialect;

    public IContentQueryAccess Access => access;

    public ContentQueryOptions Options => options;

    public short State => state;

    public string Locale => locale;

    public SiteId? Site => site;

    // The query filter on Document is off for the whole query, because EF cannot turn it off for the root alone. The trash
    // is the root's; every row reached through a relation says explicitly that it is not in it.
    public bool ReachesTrash => reachesTrash;

    // Whether the read answers the Management shape, with the pending schedules of each entry (ManagementShape).
    public bool Management => management;

    public QueryPaths Paths { get; } = new(schema, access, management);

    // A site's query reads that site's entries on every level. Global entries belong to no site, and any site may read them.
    public IQueryable<QueryRow> Rows(SchemaType type, bool trashedRoot = false)
    {
        var rows = QueryRows.Of(context, type, state, locale);
        if (trashedRoot)
        {
            rows = rows.Where(row => row.Document.DeletedAt != null);
        }
        else if (reachesTrash)
        {
            rows = rows.Where(row => row.Document.DeletedAt == null);
        }

        if (site is not { } only || type.Scope != ContentScope.Site)
        {
            return rows;
        }

        var row = Expression.Parameter(typeof(QueryRow), "row");
        Expression inSite = Expression.Equal(QueryExpressions.Column(Expression.Property(row, nameof(QueryRow.Shared)), StorageNames.SiteId, typeof(Guid)), QueryExpressions.Bind(only.Value));
        if (type.Localized)
        {
            // The locale row repeats site_id, and the unique index of a localized field leads with it: without this the
            // lookup scans that index instead of seeking in it.
            inSite = Expression.AndAlso(inSite, Expression.Equal(QueryExpressions.Column(Expression.Property(row, nameof(QueryRow.Localized)), StorageNames.SiteId, typeof(Guid)), QueryExpressions.Bind(only.Value)));
        }

        return rows.Where(Expression.Lambda<Func<QueryRow, bool>>(inSite, row));
    }

    public void Report(string path, string code, string message)
    {
        ValidationError error = new(path, code, message);
        if (!_errors.Contains(error))
        {
            _errors.Add(error);
        }
    }

    // Whether a query in this state and locale may read the rows of a type it reaches through a relation.
    public bool CanEnter(SchemaType target, bool trusted = false)
    {
        var allowed = true;
        if (state == ContentState.Draft && !trusted && !access.CanReadDraft(target))
        {
            Report("status", QueryErrorCodes.DraftNotAllowed, $"Reading drafts of {target.Name} is not allowed.");
            allowed = false;
        }

        if (target.Localized && locale.Length == 0)
        {
            Report("locale", QueryErrorCodes.InvalidParameter, $"Reading {target.Name} through a relation needs one locale.");
            allowed = false;
        }

        return allowed;
    }

    public void CountPopulated(int entries)
    {
        _populated += entries;
        if (_populated > options.MaxPopulatedEntries)
        {
            Report("populate", QueryErrorCodes.LimitExceeded, $"The query inlines more than the {options.MaxPopulatedEntries} related entries allowed.");
            ThrowIfInvalid();
        }
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors);
        }
    }
}
