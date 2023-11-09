using Microsoft.AspNetCore.Identity;

namespace uBeac.Identity
{
    public static class IdentityResultExtensions
    {
        public static void ThrowIfInvalid(this IdentityResult identityResult)
        {
            if (!identityResult.Succeeded)
            {
                var message = string.Empty;
                throw new Exception(string.Join("\r\n", identityResult.Errors.Select(x => x.Code + "," + x.Description)));
            }
        }
    }
}
