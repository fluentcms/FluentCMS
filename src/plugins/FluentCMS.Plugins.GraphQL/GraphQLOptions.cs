namespace FluentCMS.Plugins.GraphQL;

// Bound from FluentCms:GraphQL. The page size and populate depth limits are those of the REST APIs, in FluentCms:Query.
public sealed class GraphQLOptions
{
    public const string Section = "FluentCms:GraphQL";

    // The deepest a query may nest its fields. Introspection fields are not counted.
    public int MaxDepth { get; set; } = 12;

    // The most a query may cost by HotChocolate's cost analysis: the weight of the fields it resolves, and the number of
    // objects it may return.
    public double MaxFieldCost { get; set; } = 1000;

    public double MaxTypeCost { get; set; } = 1000;

    // The body of a request, in bytes. FluentCms:Limits:MaxBodySize still caps it.
    public long MaxRequestSize { get; set; } = 256 * 1024;

    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Whether any caller may read the schema by introspection. Unset, it is open in Development and elsewhere needs a
    // signed-in caller with schema.read.
    public bool? OpenIntrospection { get; set; }

    // A folder of query files, {id}.graphql, that a request names by id. A relative path is under the content root.
    public string? PersistedQueries { get; set; }

    // Refuses a query that is not in the folder.
    public bool OnlyPersisted { get; set; }

    internal string? Problem() => this switch
    {
        { MaxDepth: < 1 } => $"{nameof(MaxDepth)} must be at least 1.",
        { MaxFieldCost: not > 0 } or { MaxTypeCost: not > 0 } => $"{nameof(MaxFieldCost)} and {nameof(MaxTypeCost)} must be positive.",
        { MaxRequestSize: < 1 } => $"{nameof(MaxRequestSize)} must be at least 1.",
        _ when ExecutionTimeout <= TimeSpan.Zero => $"{nameof(ExecutionTimeout)} must be positive.",
        { OnlyPersisted: true, PersistedQueries: null or "" } => $"{nameof(OnlyPersisted)} needs {nameof(PersistedQueries)}, the folder of the queries it allows.",
        _ => null,
    };
}
