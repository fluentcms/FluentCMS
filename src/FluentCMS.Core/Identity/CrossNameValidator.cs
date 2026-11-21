using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// Sign-in takes a user name or, failing that, an address, so a name that is another user's address, or an address that is
// another user's name, would send one user's sign-in, and its failed passwords, to the other account. Only a new or changed
// name or address is checked: a pair that collided before the check existed must not stop every other save of either user,
// its lockout among them. CrossNameStartupCheck reports such pairs.
internal sealed class CrossNameValidator : IUserValidator<CmsUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<CmsUser> manager, CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var name = manager.NormalizeName(await manager.GetUserNameAsync(user));
        var email = manager.NormalizeEmail(await manager.GetEmailAsync(user));
        var stored = await manager.Users.AsNoTracking()
            .Where(other => other.Id == user.Id)
            .Select(other => new { other.NormalizedUserName, other.NormalizedEmail })
            .FirstOrDefaultAsync();

        return await CheckAsync(
            manager.Users,
            manager.ErrorDescriber,
            user,
            name != stored?.NormalizedUserName ? name : null,
            email != stored?.NormalizedEmail ? email : null,
            CancellationToken.None);
    }

    // The normalized name and address to check, each null when it did not change. CmsUserStore asks again inside the
    // transaction of the save, under the lock of the names, where no other save can pass at the same time.
    public static async Task<IdentityResult> CheckAsync(IQueryable<CmsUser> users, IdentityErrorDescriber describer, CmsUser user, string? name, string? email, CancellationToken cancellationToken)
    {
        List<IdentityError> errors = [];
        if (!string.IsNullOrEmpty(name) && await users.AnyAsync(other => other.Id != user.Id && other.NormalizedEmail == name, cancellationToken))
        {
            errors.Add(describer.DuplicateUserName(user.UserName!));
        }

        if (!string.IsNullOrEmpty(email) && await users.AnyAsync(other => other.Id != user.Id && other.NormalizedUserName == email, cancellationToken))
        {
            errors.Add(describer.DuplicateEmail(user.Email!));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}

// Reports, once the migrations ran, the users whose name is another user's address, which CrossNameValidator keeps from
// arising but which an install from before it may hold; an administrator renames one of each pair.
internal sealed class CrossNameStartupCheck(IServiceScopeFactory scopes, ILogger<CrossNameStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<CmsUser>().AsNoTracking();
        var pairs = await users
            .Join(users, named => named.NormalizedUserName, addressed => addressed.NormalizedEmail, (named, addressed) => new { Named = named.Id, Addressed = addressed.Id })
            .Where(pair => pair.Named != pair.Addressed)
            .Take(20)
            .ToListAsync(cancellationToken);
        foreach (var pair in pairs)
        {
            AccountLog.CrossNameCollision(logger, pair.Named, pair.Addressed);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
