using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Identity;

internal sealed class FirstRunSetup(
    UserManager<FluentCmsUser> users,
    RoleManager<FluentCmsRole> roles,
    TimeProvider clock) : IFirstRunSetup
{
    public async Task<bool> NeedsFirstAdministratorAsync(CancellationToken cancellationToken = default)
    {
        return !await users.Users.AnyAsync(cancellationToken);
    }

    // The one account created without an audit event: nobody exists yet to name as initiator, and the sign-in that follows is audited.
    public async Task<Result<Guid>> CreateFirstAdministratorAsync(NewUser user, CancellationToken cancellationToken = default)
    {
        if (!await NeedsFirstAdministratorAsync(cancellationToken))
        {
            return Result<Guid>.Failure(UserErrorCodes.AlreadySetUp, "An administrator already exists; sign in instead.");
        }

        return await Accounts.CreateAdministratorAsync(users, roles, clock, user);
    }
}
