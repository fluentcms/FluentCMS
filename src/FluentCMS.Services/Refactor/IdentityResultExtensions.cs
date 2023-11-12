using FluentCMS.Services.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public static class IdentityResultExtensions
{
    public static void ThrowIfInvalid(this IdentityResult identityResult)
    {
        if (!identityResult.Succeeded)
        {
            var message = string.Empty;
            throw new AppException(identityResult.Errors.Select(e =>
                new AppError(ErrorType.BadRequest, ErrorArea.Users, e.Code, e.Description)));
        }
    }
}
