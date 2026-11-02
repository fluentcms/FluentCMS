using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// What one compilation shares: the state and locale every joined row must have, and the errors found so far.
internal sealed class QueryScope(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options, short state, string locale)
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

    public QueryPaths Paths { get; } = new(schema, access);

    public IQueryable<QueryRow> Rows(SchemaType type) => QueryRows.Of(context, type, state, locale);

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
