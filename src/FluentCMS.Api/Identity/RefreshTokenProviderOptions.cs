using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Api.Identity;

public class RefreshTokenProviderOptions: DataProtectionTokenProviderOptions
{
    public const string RefreshTokenProviderName = "RefreshTokenProvider";

    public RefreshTokenProviderOptions()
    {
        base.Name = RefreshTokenProviderName;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}
