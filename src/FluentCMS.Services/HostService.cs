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

public class HostService : IHostService
{
    private readonly IHostRepository _hostRepository;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationContext _applicationContext;

    public HostService(IHostRepository hostRepository, IUserRepository userRepository, IApplicationContext applicationContext)
    {
        _hostRepository = hostRepository;
        _userRepository = userRepository;
        _applicationContext = applicationContext;
    }

    public async Task<Host> Create(Host host, CancellationToken cancellationToken = default)
    {
        // checking if host record exists or not. if exists, throw exception
        // this should be called only for the first time on installation
        var hosts = await _hostRepository.GetAll(cancellationToken);
        if (hosts.Any())
            throw new Exception("Host already exists");

        await CheckSuperUsers(host, cancellationToken);

        host.CreatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;
        host.LastUpdatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;

        return await _hostRepository.Create(host, cancellationToken) ?? throw new Exception("Host not created");
    }

    public async Task<Host> Update(Host host, CancellationToken cancellationToken = default)
    {
        await CheckSuperUsers(host, cancellationToken);

        // throw exception for guest user
        if (_applicationContext.Current.User == null)
            throw new Exception("You don't have enough permission to do the operation");

        var currentUsername = _applicationContext.Current.User.Username;

        // checking current user is super user or not
        var hosts = await _hostRepository.GetAll(cancellationToken);
        var oldHost = hosts.Single();
        if (oldHost.SuperUsers.Contains(currentUsername))
            throw new Exception("You don't have enough permission to do the operation");

        // super user can't remove himself from super user list
        if (!host.SuperUsers.Contains(_applicationContext.Current.User.Username))
            throw new Exception("You can't remove yourself from super user list");

        // setting id from old host to the updated one
        host.Id = oldHost.Id;

        host.LastUpdatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;

        return await _hostRepository.Update(host, cancellationToken) ?? throw new Exception("Host not updated");
    }

    public async Task<Host> Get(CancellationToken cancellationToken = default)
    {
        // throw exception for guest user
        if (_applicationContext.Current.User == null)
            throw new Exception("You don't have enough permission to do the operation");

        var currentUsername = _applicationContext.Current.User.Name;

        var hosts = await _hostRepository.GetAll(cancellationToken);

        if (!hosts.Any() || !hosts.First().SuperUsers.Contains(currentUsername))
            throw new Exception("Host not found");

        return hosts.Single();
    }


    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetAll(cancellationToken);
        return hosts.Any();
    }

    private static async Task CheckSuperUsers(Host host, CancellationToken cancellationToken = default)
    {
        // host should have at least one super user
        if (host.SuperUsers.Count == 0)
            throw new Exception("Host should have at least one super user");

        // check if user ids are valid
        // TODO: check if user ids are valid
        //var users = await _userRepository.GetByIds(host.SuperUserIds, cancellationToken);
        //if (users.Count() != host.SuperUserIds.Count)
        //    throw new Exception($"One or some user ids are not found");

        await Task.CompletedTask;
    }

}
