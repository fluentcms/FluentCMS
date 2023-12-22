using FluentCMS.Repositories.Abstraction;

namespace FluentCMS.Services;

public interface IAppTemplateService
{
    Task<IEnumerable<AppTemplate>> GetAll(CancellationToken cancellationToken = default);
    Task<AppTemplate> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<AppTemplate> Create(AppTemplate appTemplate, CancellationToken cancellationToken = default);
}


public class AppTemplateService(IAppTemplateRepository appTemplateRepository) : IAppTemplateService
{

    public async Task<IEnumerable<AppTemplate>> GetAll(CancellationToken cancellationToken = default)
    {
        return await appTemplateRepository.GetAll(cancellationToken);
    }

    public async Task<AppTemplate> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await appTemplateRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppNotFound);
    }

    public async Task<AppTemplate> Create(AppTemplate appTemplate, CancellationToken cancellationToken = default)
    {
        return await appTemplateRepository.Create(appTemplate, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppUnableToCreate);
    }
}
