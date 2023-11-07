using FluentCMS.Entities.Users;
using FluentCMS.Repositories.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity;

public class FluentCmsUserStore : IUserStore<User>, IUserPasswordStore<User>,IUserSecurityStampStore<User>
{
    private readonly IUserService _userService;
    private readonly IPasswordHasher<User> _hasher;
    private static Dictionary<Guid,string> _inMemorySecurityStampStore = new Dictionary<Guid, string>();

    public FluentCmsUserStore(IUserService userService, IPasswordHasher<User> hasher)
    {
        _userService = userService;
        _hasher = hasher;
    }
    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _userService.Create(user);
            return IdentityResult.Success;
        }
        catch (ApplicationException ex)
        {
            // todo: implement error codes
            return IdentityResult.Failed(new IdentityError() { Code = "", Description = ex.Message });
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _userService.Delete(user);
            return IdentityResult.Success;
        }
        catch (ApplicationException ex)
        {
            // todo: implement error codes
            return IdentityResult.Failed(new IdentityError() { Code = "", Description = ex.Message });
        }
        catch (Exception)
        {
            throw;
        }
    }

    public void Dispose()
    {
        // no action required
    }

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        return await _userService.GetById(Guid.Parse(userId));
    }

    public async Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        try
        {
            return await _userService.GetByUsername(normalizedUserName);
        }
        catch (Exception)
        {

            return null;
        }
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user?.Username.ToLower());
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user?.Password);
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken)
    {
        string? stamp = _inMemorySecurityStampStore.GetValueOrDefault(user.Id);
        if(stamp == null)
        {
            var newStamp = Guid.NewGuid().ToString("N");
            _inMemorySecurityStampStore.Add(user.Id, newStamp);
            stamp = newStamp;
        }
        return Task.FromResult(stamp??null);
    }

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user?.Id.ToString()!);
    }

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user?.Username);
    }

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(user?.Password));
    }

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken)
    {
        // not supported
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.Password = passwordHash??"";
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        _inMemorySecurityStampStore.Add(user.Id, stamp);
        return Task.CompletedTask;
    }

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        // Should this method update user in db?
        user.Username = userName ?? throw new ArgumentException("userName cannot be null");
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _userService.Edit(user);
            return IdentityResult.Success;
        }
        catch (ApplicationException ex)
        {
            return IdentityResult.Failed(new IdentityError() { Code = "", Description = ex.Message });
        }
    }
}