using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Role entities in MongoDB.
/// Extends SiteAssociatedRepository to handle roles associated with specific sites in a multi-tenant environment.
/// </summary>
public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    /// <summary>
    /// Initializes a new instance of the RoleRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public RoleRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

}
