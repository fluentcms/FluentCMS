namespace FluentCMS.Media;

/// <summary>
/// Reads assets as content shows them: their metadata, with the alternative text and caption in a locale, and the signed
/// URLs that serve them, as the Delivery API gives them. Population of a <c>media</c> field and the rich text renderers use
/// it. On the Delivery API (<see cref="Content.OperationContext.IsDelivery"/>) it gives the public assets, whoever reads,
/// as the Delivery API's media route serves them to anyone, and never a private one. Elsewhere it needs <c>media.read</c>
/// and keeps to the folders that a grant limited to folders reaches, so an asset the caller cannot read is left out, and a
/// private asset has no URLs. An asset of another site's library and one that is deleted are always left out.
/// </summary>
public interface IMediaResolver
{
    /// <summary>Resolves assets in the site of the current operation.</summary>
    /// <param name="ids">The ids of the assets.</param>
    /// <param name="locale">The locale of the alternative text and caption, or <see langword="null"/> for that of the operation.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The assets the caller may read, by id.</returns>
    Task<IReadOnlyDictionary<Guid, MediaDelivery>> ResolveAsync(IReadOnlyCollection<Guid> ids, string? locale, CancellationToken cancellationToken = default);
}
