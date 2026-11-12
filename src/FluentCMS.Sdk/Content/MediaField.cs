namespace FluentCMS.Content;

/// <summary>A field of the <c>media</c> type of the media plugin, single or multiple, filtered by asset id. It cannot be sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class MediaField(string path) : QueryField(path)
{
    /// <summary>The field holds the asset (<c>eq</c>).</summary>
    /// <param name="assetId">The id of the asset.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(Guid assetId) => Filter(FilterOperator.Equal, assetId);

    /// <summary>The field holds one of the assets (<c>in</c>).</summary>
    /// <param name="assetIds">The ids of the assets.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<Guid> assetIds) => Filter(FilterOperator.In, List(assetIds));
}
