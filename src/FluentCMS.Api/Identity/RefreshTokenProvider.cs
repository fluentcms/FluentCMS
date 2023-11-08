using FluentCMS.Entities.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Api.Identity;

public class RefreshTokenProvider : DataProtectorTokenProvider<User>
{
    public RefreshTokenProvider(IDataProtectionProvider dataProtectionProvider, IOptions<RefreshTokenProviderOptions> options, ILogger<DataProtectorTokenProvider<User>> logger) : base(dataProtectionProvider, options, logger)
    {
    }
}
