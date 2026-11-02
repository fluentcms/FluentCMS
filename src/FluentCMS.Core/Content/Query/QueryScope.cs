using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// What one compilation shares: the state and locale every joined row must have, and the errors found so far.
internal sealed class QueryScope(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options, short state, string locale)
{
    private readonly List<ValidationError> _errors = [];

    public CmsDbContext Context => context;

    public SchemaModel Schema => schema;

    public IJsonSqlDialect Dialect => dialect;

    public IContentQueryAccess Access => access;

    public ContentQueryOptions Options => options;

    public short State => state;

    public string Locale => locale;

    public QueryPaths Paths { get; } = new(schema, access);

    public IQueryable<QueryRow> Rows(SchemaType type) => QueryRows.Of(context, type, state, locale);

    public void Report(string path, string code, string message) => _errors.Add(new(path, code, message));

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationException(_errors);
        }
    }
}
