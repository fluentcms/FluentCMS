using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

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
            throw new Exception("Host already initialized");

        PrepareForCreate(host);

        return await _hostRepository.Create(host, cancellationToken) ??
            throw new Exception("Host not created");
    }

    public async Task<Host> Update(Host host, CancellationToken cancellationToken = default)
    {
        // TODO: move this to a validator
        CheckSuperUsers(host);

        // checking current user is super user or not
        var hosts = await _hostRepository.GetAll(cancellationToken);
        var oldHost = hosts.Single();
        if (!Current.IsSuperAdmin)
            throw new Exception("You don't have enough permission to do the operation");

        // super user can't remove himself from super user list
        if (!host.SuperUsers.Contains(Current.UserName))
            throw new Exception("You can't remove yourself from super user list");

        // setting id from old host to the updated one
        host.Id = oldHost.Id;

        PrepareForUpdate(host);

        return await _hostRepository.Update(host, cancellationToken)
            ?? throw new Exception("Host not updated");
    }

    public async Task<Host> Get(CancellationToken cancellationToken = default)
    {
        // throw exception for guest user
        if (Current.IsSuperAdmin)
            throw new Exception("You don't have enough permission to do the operation");

        var hosts = await _hostRepository.GetAll(cancellationToken);

        if (!hosts.Any())
            throw new Exception("Host not found");

        return hosts.Single();
    }

    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetAll(cancellationToken);
        return hosts.Any();
    }

    private void CheckSuperUsers(Host host)
    {
        // host should have at least one super user
        if (host.SuperUsers.Count == 0)
            throw new Exception("Host should have at least one super user");
    }

}
