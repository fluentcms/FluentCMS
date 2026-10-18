using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Infrastructure.Identity;

// What the first-run setup and the directory share: how an account is created and how a disabled one is recognised.
internal static class Accounts
{
    // Identity has no "disabled" flag; a lockout with no end is the standard way to switch an account off.
    // A lockout after failed sign-ins ends within minutes, so it does not count as disabled.
    public static readonly DateTimeOffset NoEnd = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsDisabled(FluentCmsUser user)
    {
        return user.LockoutEnabled && user.LockoutEnd is { } end && end >= NoEnd;
    }

    public static async Task<Result<Guid>> CreateAdministratorAsync(
        UserManager<FluentCmsUser> users,
        RoleManager<FluentCmsRole> roles,
        TimeProvider clock,
        NewUser newUser)
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

    public static IEnumerable<Error> ToErrors(IdentityResult result)
    {
        return result.Errors.Select(error => new Error(error.Code, error.Description));
    }
}
