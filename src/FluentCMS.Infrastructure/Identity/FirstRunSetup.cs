using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Identity;

internal sealed class FirstRunSetup(UserManager<FluentCmsUser> users, IUserDirectory directory) : IFirstRunSetup
{
    public async Task<bool> NeedsFirstAdministratorAsync(CancellationToken cancellationToken = default)
    {
        return !await users.Users.AnyAsync(cancellationToken);
    }

    public async Task<Result<Guid>> CreateFirstAdministratorAsync(NewUser user, CancellationToken cancellationToken = default)
    {
        if (!await NeedsFirstAdministratorAsync(cancellationToken))
        {
            return Result<Guid>.Failure("already_set_up", "An administrator already exists; sign in instead.");
        }

        return await directory.CreateAsync(user, cancellationToken);
    }
}
