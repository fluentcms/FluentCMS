using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public static class IdentityResultExtensions
{
    public static void ThrowIfInvalid(this IdentityResult identityResult)
    {
        if (!identityResult.Succeeded)
        {
            throw new AppException(identityResult.Errors.Select(e => $"User.{e.Code}"));
        }
    }
}
