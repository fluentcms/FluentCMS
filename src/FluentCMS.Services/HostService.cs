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

public class HostService(
    IHostRepository hostRepository,
    IApplicationContext appContext,
    IAuthorizationProvider authorizationProvider) : IHostService
{

    public async Task<Host> Create(Host host, CancellationToken cancellationToken = default)
    {
        // There is no need to check for super admin here
        // Because this method will be called only once on installation
        // Host should have at least one super user
        CheckSuperUsers(host);

        // Checking if host record exists or not. if exists, throw exception
        // this should be called only for the first time on installation
        if (await IsInitialized(cancellationToken))
            throw new AppException(ExceptionCodes.HostAlreadyInitialized);

        return await hostRepository.Create(host, cancellationToken) ??
            throw new AppException(ExceptionCodes.HostUnableToCreate);
    }

    public async Task<Host> Update(Host host, CancellationToken cancellationToken = default)
    {
        // Host should have at least one super user
        CheckSuperUsers(host);

        _ = await hostRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);

        // checking current user is super user or not
        if (!authorizationProvider.IsSuperAdmin())
            throw new AppPermissionException();

        // super admin can't remove himself from super user list
        if (!host.SuperUsers.Contains(appContext.Current.UserName))
            throw new AppException(ExceptionCodes.HostUnableToRemoveYourself);

        return await hostRepository.Update(host, cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);
    }

    public async Task<Host> Get(CancellationToken cancellationToken = default)
    {
        // throw exception for all users except super admins
        if (!authorizationProvider.IsSuperAdmin())
            throw new AppPermissionException();

        return await hostRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.HostNotFound);
    }

    private static void CheckSuperUsers(Host host)
    {
        // host should have at least one super user
        if (host.SuperUsers.Count == 0)
            throw new AppException(ExceptionCodes.HostAtLeastOneSuperUser);
    }

    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        var host = await hostRepository.Get(cancellationToken);
        return host != null;
    }
}
