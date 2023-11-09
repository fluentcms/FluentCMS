using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using FluentCMS.Entities.Identity;
using FluentCMS.Repositories.Identity.Abstractions;

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

    public async Task<IList<Claim>> GetClaimsAsync(Role role, CancellationToken cancellationToken = default)
    {
        if (role.Claims is null)
            return await Task.FromResult(new List<Claim>());

        return role.Claims.Select(e => new Claim(e.ClaimType ?? string.Empty, e.ClaimValue ?? string.Empty)).ToList();
    }

    public Task AddClaimAsync(Role role, Claim claim, CancellationToken cancellationToken = default)
    {
        if (role.Claims is null)
            role.Claims = new List<IdentityRoleClaim<Guid>>();

        var currentClaim = role.Claims.FirstOrDefault(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);

        if (currentClaim == null)
        {
            var identityRoleClaim = new IdentityRoleClaim<Guid>()
            {
                ClaimType = claim.Type,
                ClaimValue = claim.Value,
                RoleId = role.Id,
                Id = 0
            };

            role.Claims.Add(identityRoleClaim);
        }
        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(Role role, Claim claim, CancellationToken cancellationToken = default)
    {
        role.Claims.RemoveAll(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);
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
