using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Layout entities in MongoDB.
/// Extends SiteAssociatedRepository for handling entities associated with a specific site.
/// </summary>
public class LayoutRepository : SiteAssociatedRepository<Layout>, ILayoutRepository
{
    /// <summary>
    /// Initializes a new instance of the LayoutRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public LayoutRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }
}
