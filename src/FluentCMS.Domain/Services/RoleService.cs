using Ardalis.GuardClauses;
using FluentCMS.Entities.Users;
using FluentCMS.Repository;

namespace FluentCMS.Services;

public class RoleService
{
    private readonly IGenericRepository<Role> _roleRepository;

    public RoleService(IGenericRepository<Role> roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<IEnumerable<Role>> GetAll()
    {
        var roles = await _roleRepository.GetAll();
        return roles;
    }

    public async Task<Role> GetById(Guid id)
    {
        var roles = await _roleRepository.GetById(id)
            ?? throw new ApplicationException("Requested role does not exists.");
        return roles;
    }

    public Task Create(Role role, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(role);
        Guard.Against.NullOrWhiteSpace(role.Name);

        return _roleRepository.Create(role, cancellationToken);
    }

    public async Task Update(Role role, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(role);
        Guard.Against.Default(role.Id);
        Guard.Against.NullOrWhiteSpace(role.Name);

        var oldRole = await _roleRepository.GetById(role.Id);
        if (oldRole == null)
            throw new ApplicationException("Provided RoleId does not exists.");

        await _roleRepository.Update(role, cancellationToken);
    }

    public async Task Delete(Role role, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(role);
        Guard.Against.Default(role.Id);

        var oldRole = await _roleRepository.GetById(role.Id);
        if (oldRole == null)
            throw new ApplicationException("Provided RoleId does not exists.");

        await _roleRepository.Delete(role.Id, cancellationToken);
    }
}
