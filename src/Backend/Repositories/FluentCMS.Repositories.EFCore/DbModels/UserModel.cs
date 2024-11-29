﻿using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Users")]
public class UserModel : IdentityUser<Guid>, IAuditableEntityModel
{
    public DateTime? LoginAt { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool Enabled { get; set; } = true;
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];
    public List<UserTwoFactorRecoveryCode> RecoveryCodes { get; set; } = [];
    public string? AuthenticatorKey { get; set; }
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
