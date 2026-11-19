using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// Sign-in takes a user name or, failing that, an address, so a name that is another user's address, or an address that is
// another user's name, would send one user's sign-in, and its failed passwords, to the other account.
internal sealed class CrossNameValidator : IUserValidator<CmsUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<CmsUser> manager, CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var name = manager.NormalizeName(await manager.GetUserNameAsync(user));
        var email = manager.NormalizeEmail(await manager.GetEmailAsync(user));
        List<IdentityError> errors = [];
        if (!string.IsNullOrEmpty(name) && await manager.Users.AnyAsync(other => other.Id != user.Id && other.NormalizedEmail == name))
        {
            errors.Add(manager.ErrorDescriber.DuplicateUserName(user.UserName!));
        }

        if (!string.IsNullOrEmpty(email) && await manager.Users.AnyAsync(other => other.Id != user.Id && other.NormalizedUserName == email))
        {
            errors.Add(manager.ErrorDescriber.DuplicateEmail(user.Email!));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}
