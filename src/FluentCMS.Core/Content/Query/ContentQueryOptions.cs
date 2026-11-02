namespace FluentCMS.Core.Content.Query;

public sealed class ContentQueryOptions
{
    public int DefaultPageSize { get; init; } = 20;

    public int MaxPageSize { get; init; } = 100;

    // Also caps the relation hops of a filter or selection path.
    public int MaxPopulateDepth { get; init; } = 3;

    // Conditions, logical nodes and the items of a list operand, all counted.
    public int MaxFilterNodes { get; init; } = 100;

    // Entries inlined by population in one query, at every level: a page of 100 entries multiplies at each depth.
    public int MaxPopulatedEntries { get; init; } = 1000;
}
