using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content.Query;

// What one compilation shares: the state, locale and site every joined row must have, and the errors found so far.
internal sealed class QueryScope(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options, short state, string locale, SiteId? site, bool reachesTrash = false, bool management = false)
{
    private Tally _tally = new();
    private QueryScope? _targets;

    // Where the targets of relations, and the entry links of rich text, are read and populated at every level: this scope,
    // or for a preview token one of published rows as its minter reads them, which has the errors and the count of
    // populated entries of this one, so that the caps hold for the whole read.
    public QueryScope Targets => _targets ??= access.PublishedTargets is { } published
        ? new QueryScope(context, schema, dialect, published, options, ContentState.Published, locale, site, reachesTrash) { _tally = _tally, Links = Links, Budget = Budget }
        : this;

    // What the request has populated, in this read and in the other locales it reads.
    public PopulateBudget Budget { get; init; } = new();

    // What a read drops from stored rich text (RichTextLinks).
    public SafeUrl Links { get; init; } = SafeUrl.Default;

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

    // The same read of another locale, as the principal reads that one, with the errors and the populate count of this one.
    // What it refuses names that locale, unless this read refused the same already.
    public QueryScope InLocale(string other, IContentQueryAccess otherAccess) =>
        new(context, schema, dialect, otherAccess, options, state, other, site, reachesTrash, management) { _tally = _tally, Links = Links, Budget = Budget, _namesLocale = true };

    private bool _namesLocale;

    public IQueryable<QueryRow> Rows(SchemaType type, bool trashedRoot = false) => Rows(type, QueryRows.Of(context, type, state, locale), trashedRoot);

    // The rows of a localized type in any of these locales, an entry once for each locale it has.
    public IQueryable<QueryRow> Rows(SchemaType type, IReadOnlyList<string> locales) => Rows(type, QueryRows.Of(context, type, state, locales), trashedRoot: false);

    // A site's query reads that site's entries on every level. Global entries belong to no site, and any site may read them.
    private IQueryable<QueryRow> Rows(SchemaType type, IQueryable<QueryRow> rows, bool trashedRoot)
    {
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
        if (_namesLocale)
        {
            if (_tally.Errors.Any(error => error.Path == path && error.Code == code))
            {
                return;
            }

            message = $"{message} This holds in the locale '{locale}', which a list of every locale reads too.";
        }

        ValidationError error = new(path, code, message);
        if (!_tally.Errors.Contains(error))
        {
            _tally.Errors.Add(error);
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
        Budget.Populated += entries;
        if (Budget.Populated > options.MaxPopulatedEntries)
        {
            Report("populate", QueryErrorCodes.LimitExceeded, $"The query inlines more than the {options.MaxPopulatedEntries} related entries allowed.");
            ThrowIfInvalid();
        }
    }

    public void ThrowIfInvalid()
    {
        if (_tally.Errors.Count > 0)
        {
            throw new ValidationException(_tally.Errors);
        }
    }

    private sealed class Tally
    {
        public List<ValidationError> Errors { get; } = [];
    }
}
