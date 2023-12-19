using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IAppService
{
    Task<IEnumerable<App>> GetAll(CancellationToken cancellationToken = default);
    Task<App> GetBySlug(string appSlug, CancellationToken cancellationToken = default);
    Task<App> Create(App app, CancellationToken cancellationToken = default);
    Task<App> Update(App app, CancellationToken cancellationToken = default);
}

public class AppService(IAppRepository appRepository) : IAppService
{

    public async Task<IEnumerable<App>> GetAll(CancellationToken cancellationToken = default)
    {
        return await appRepository.GetAll(cancellationToken);
    }

    public async Task<App> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await appRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppNotFound);
    }

    public async Task<App> GetBySlug(string appSlug, CancellationToken cancellationToken = default)
    {
        // There is no need to check for permissions here
        return await appRepository.GetBySlug(appSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppNotFound);
    }

    public async Task<App> Create(App app, CancellationToken cancellationToken = default)
    {
        return await appRepository.Create(app, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToCreate);
    }

    public async Task<App> Update(App app, CancellationToken cancellationToken = default)
    {
        return await appRepository.Update(app, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToUpdate);
    }
}
