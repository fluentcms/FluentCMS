using FluentCMS.Entities;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace FluentCMS.Services.Identity.Stores;

public class RoleStore : IRoleClaimStore<Role>, IQueryableRoleStore<Role>
{
    private readonly IRoleRepository _repository;

    public IQueryable<Role> Roles => _repository.AsQueryable();

    public RoleStore(IRoleRepository repository)
    {
        _repository = repository;
    }

    public async Task<IdentityResult> CreateAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.Create(role, cancellationToken);

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.Update(role, cancellationToken);

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(Role role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.Delete(role.Id, cancellationToken);

        return IdentityResult.Success;
    }

    public Task<string> GetRoleIdAsync(Role role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.Id.ToString());
    }

    public Task<string?> GetRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.Name);
    }

    public Task SetRoleNameAsync(Role role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(Role role, CancellationToken cancellationToken)
    {
        return Task.FromResult(role.NormalizedName);
    }

    public Task SetNormalizedRoleNameAsync(Role role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<Role?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.Parse(roleId);

        return await _repository.GetById(id, cancellationToken);
    }

    public Task<Role?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _repository.FindByName(normalizedRoleName, cancellationToken);
    }

    public Task<IList<Claim>> GetClaimsAsync(Role role, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<Claim>() as IList<Claim>);
    }

    public Task AddClaimAsync(Role role, Claim claim, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(Role role, Claim claim, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
