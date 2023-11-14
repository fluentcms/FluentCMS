using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

public class User : IdentityUser<Guid>, IAuditEntity
{
    public DateTime? LastLoginAt { get; set; }
    public int LoginsCount { get; set; }
    public DateTime? LastPasswordChangedAt { get; set; }
    public string LastPasswordChangedBy { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }

    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }

    public string AuthenticatorKey { get; set; } = string.Empty;
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];
    public virtual List<TwoFactorRecoveryCode> RecoveryCodes { get; set; } = [];

    public List<Guid> RoleIds { get; set; } = [];

    public User()
    {
    }

    public User(string userName) : base(userName)
    {
    }
}

public class TwoFactorRecoveryCode
{
    public string? Code { get; set; }
    public bool Redeemed { get; set; }
}
