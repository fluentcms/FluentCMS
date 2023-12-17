using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

/// <summary>
/// Extension methods for IdentityResult to handle validation errors.
/// </summary>
public static class IdentityResultExtensions
{
    /// <summary>
    /// Throws an exception if the IdentityResult is not successful, indicating validation errors.
    /// </summary>
    /// <param name="identityResult">The IdentityResult to check.</param>
    /// <exception cref="AppException">Thrown when the IdentityResult indicates validation errors.</exception>
    public static void ThrowIfInvalid(this IdentityResult identityResult)
    {
        if (!identityResult.Succeeded)
        {
            throw new AppException(identityResult.Errors.Select(e => $"User.{e.Code}"));
        }
    }
}
