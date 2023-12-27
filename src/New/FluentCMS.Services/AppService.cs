namespace FluentCMS.Services;

public interface IAppService : IService
{
    Task<IEnumerable<App>> GetAll(CancellationToken cancellationToken = default);
    Task<App> GetBySlug(string appSlug, CancellationToken cancellationToken = default);
    Task<App> Create(App app, CancellationToken cancellationToken = default);
    Task<App> Update(App app, CancellationToken cancellationToken = default);
    Task<App> Delete(Guid appId, CancellationToken cancellationToken = default);
}

public class AppService(IAppRepository appRepository) : IAppService
{

    public async Task<IEnumerable<App>> GetAll(CancellationToken cancellationToken = default)
    {
        return await appRepository.GetAll(cancellationToken);
    }

    public async Task<App> GetBySlug(string appSlug, CancellationToken cancellationToken = default)
    {
        // There is no need to check for permissions here
        return await appRepository.GetBySlug(appSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppNotFound);
    }

    public async Task<App> Create(App app, CancellationToken cancellationToken = default)
    {
        // check if slug is unique
        var existing = await appRepository.GetBySlug(app.Slug, cancellationToken);
        if (existing != null)
            throw new AppException(ExceptionCodes.AppSlugNotUnique);

        return await appRepository.Create(app, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToCreate);
    }

    public async Task<App> Update(App app, CancellationToken cancellationToken = default)
    {
        // check if slug is unique
        var existing = await appRepository.GetBySlug(app.Slug, cancellationToken);
        if (existing != null && existing.Id != app.Id)
            throw new AppException(ExceptionCodes.AppSlugNotUnique);

        return await appRepository.Update(app, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToUpdate);
    }

    public async Task<App> Delete(Guid appId, CancellationToken cancellationToken = default)
    {
        return await appRepository.Delete(appId, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToDelete);
    }
}
