using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IHostService
{
    Task<Host> Create(Host host, CancellationToken cancellationToken = default);
    Task<Host> Update(Host host, CancellationToken cancellationToken = default);
    Task<Host> Get(CancellationToken cancellationToken = default);
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
}

public class HostService : BaseService<Host>, IHostService
{
    private readonly IHostRepository _hostRepository;

    public HostService(IHostRepository hostRepository, IApplicationContext appContext) : base(appContext)
    {
        _hostRepository = hostRepository;
    }

    public async Task<Host> Create(Host host, CancellationToken cancellationToken = default)
    {
        // TODO: move this to a validator
        CheckSuperUsers(host);

        // checking if host record exists or not. if exists, throw exception
        // this should be called only for the first time on installation
        if (await IsInitialized(cancellationToken))
            throw new AppException(ExceptionCodes.HostAlreadyInitialized);

        return await _hostRepository.Create(host, cancellationToken) ??
            throw new AppException(ExceptionCodes.HostUnableToCreate);
    }

    public async Task<Host> Update(Host host, CancellationToken cancellationToken = default)
    {
        // TODO: move this to a validator
        CheckSuperUsers(host);

        var oldHost = await _hostRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);

        // checking current user is super user or not
        if (!Current.IsSuperAdmin)
            throw new AppPermissionException();

        // super user can't remove himself from super user list
        if (!host.SuperUsers.Contains(Current.UserName))
            throw new AppException(ExceptionCodes.HostUnableToRemoveYourself);

        // setting id from old host to the updated one
        host.Id = oldHost.Id;

        return await _hostRepository.Update(host, cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);
    }

    public async Task<Host> Get(CancellationToken cancellationToken = default)
    {
        // throw exception for all users except super admins
        if (!Current.IsSuperAdmin)
            throw new AppPermissionException();

        return await _hostRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostNotFound);
    }

    private void CheckSuperUsers(Host host)
    {
        // host should have at least one super user
        if (host.SuperUsers.Count == 0)
            throw new AppException(ExceptionCodes.HostAtLeastOneSuperUser);
    }

    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.Get(cancellationToken);
        return host != null;            
    }
}
