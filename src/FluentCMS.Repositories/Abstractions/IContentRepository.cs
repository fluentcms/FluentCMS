using FluentCMS.Entities;

namespace FluentCMS.Repositories;

/// <summary>
/// Defines a repository interface for managing content entities associated with a specific site within the FluentCMS system.
/// This interface extends the capabilities of ISiteAssociatedRepository with content-specific functionalities.
/// </summary>
/// <typeparam name="TContent">The type of content entity managed by this repository. 
/// TContent must be derived from the Content class and must have a parameterless constructor.</typeparam>
public interface IContentRepository<TContent> : ISiteAssociatedRepository<TContent> where TContent : Content, new()
{
    /// <summary>
    /// Asynchronously retrieves all content entities of a specified type associated with a given site.
    /// </summary>
    /// <param name="contentType">The type of content to retrieve.</param>
    /// <param name="siteId">The unique identifier of the site for which content is to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the retrieval operation.</param>
    /// <returns>A task representing the asynchronous operation, containing an enumeration of content entities of the specified type.</returns>
    Task<IEnumerable<TContent>> GetAllForSite(string contentType, Guid siteId, CancellationToken cancellationToken = default);
}
