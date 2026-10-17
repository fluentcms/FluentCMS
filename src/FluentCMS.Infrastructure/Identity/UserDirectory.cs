using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Identity;

internal sealed class UserDirectory(
    UserManager<FluentCmsUser> users,
    RoleManager<FluentCmsRole> roles,
    TimeProvider clock) : IUserDirectory
{
    // Identity has no "disabled" flag; a lockout with no end is the standard way to switch an account off.
    private static readonly DateTimeOffset NoEnd = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await users.Users.OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var summaries = new List<UserSummary>(stored.Count);

        foreach (var user in stored)
        {
            summaries.Add(new UserSummary(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                IsDisabled(user),
                (await users.GetRolesAsync(user)).ToList()));
        }

        return summaries;
    }

    public async Task<Result<Guid>> CreateAsync(NewUser newUser, CancellationToken cancellationToken = default)
    {
        var user = new FluentCmsUser
        {
            Id = Guid.NewGuid(),
            UserName = newUser.Email,
            Email = newUser.Email,
            DisplayName = newUser.DisplayName,
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await users.CreateAsync(user, newUser.Password);
        if (!created.Succeeded)
        {
            return Result<Guid>.Failure(ToErrors(created));
        }

        if (!await roles.RoleExistsAsync(FluentCmsRoles.Administrator))
        {
            await roles.CreateAsync(new FluentCmsRole { Id = Guid.NewGuid(), Name = FluentCmsRoles.Administrator });
        }

        var assigned = await users.AddToRoleAsync(user, FluentCmsRoles.Administrator);
        return assigned.Succeeded
            ? Result<Guid>.Success(user.Id)
            : Result<Guid>.Failure(ToErrors(assigned));
    }

    public async Task<Result> DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("user_not_found", $"No user has the id {userId}.");
        }

        var enabled = await users.SetLockoutEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return Result.Failure(ToErrors(enabled));
        }

        var locked = await users.SetLockoutEndDateAsync(user, NoEnd);
        return locked.Succeeded ? Result.Success() : Result.Failure(ToErrors(locked));
    }

    private bool IsDisabled(FluentCmsUser user)
    {
        return user.LockoutEnabled && user.LockoutEnd is { } end && end > clock.GetUtcNow();
    }

    private static IEnumerable<Error> ToErrors(IdentityResult result)
    {
        return result.Errors.Select(error => new Error(error.Code, error.Description));
    }
}
