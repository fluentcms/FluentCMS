using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Content.Query;

// FluentCms:Query.
public sealed class ContentQueryOptions
{
    public const string Section = "Query";

    [Range(1, int.MaxValue)]
    public int DefaultPageSize { get; init; } = 20;

    [Range(1, int.MaxValue)]
    public int MaxPageSize { get; init; } = 100;

    // Also caps the relation hops of a filter or selection path.
    [Range(0, int.MaxValue)]
    public int MaxPopulateDepth { get; init; } = 3;

    // Conditions, logical nodes and the items of a list operand, all counted.
    [Range(1, int.MaxValue)]
    public int MaxFilterNodes { get; init; } = 100;

    // Entries inlined by population in one query, at every level: a page of 100 entries multiplies at each depth.
    [Range(1, int.MaxValue)]
    public int MaxPopulatedEntries { get; init; } = 1000;

    internal void Validate()
    {
        if (DefaultPageSize > MaxPageSize)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(DefaultPageSize)} is {DefaultPageSize}; it must not be more than {nameof(MaxPageSize)}, {MaxPageSize}.");
        }
    }
}
