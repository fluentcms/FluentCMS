using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Identity's Data Protection tokens with a lifetime of their own (cms.Users.PasswordResetLifetime). A token carries the
// user's security stamp, which the reset changes, so it is spent once it is used.
internal sealed class PasswordResetTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<CmsUser>> logger)
    : DataProtectorTokenProvider<CmsUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "FluentCmsPasswordReset";
}

internal sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions;
