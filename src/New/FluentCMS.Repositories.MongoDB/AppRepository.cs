namespace FluentCMS.Repositories.MongoDB;

public class AppRepository : AppAssociatedRepository<App>, IAppRepository
{
    public AppRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public override async Task<App?> Create(App entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newSite = await base.Create(entity, cancellationToken);

        if (newSite == null)
            return null;

        newSite.AppId = newSite.Id;

        return await Update(newSite, cancellationToken);
    }

    public async Task<App?> GetBySlug(string appSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<App>.Filter.Eq(x => x.Slug, appSlug);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
