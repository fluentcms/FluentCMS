namespace FluentCMS.Identity;

public class User : User<UserClaim, UserLogin, UserToken>
{
}

public class User<TUserClaim, TUserLogin, TUserToken> : IdentityUser<Guid>, IAuditableEntity
    where TUserClaim : UserClaim, new()
    where TUserLogin : UserLogin, new()
    where TUserToken : UserToken, new()
{
    // IAuditableEntity implementations
    public string? CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }

    // Additional properties
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool Enabled { get; set; } = true;
    public string? AuthenticatorKey { get; set; }
    public string Description { get; set; } = string.Empty;

    public User()
    {
    }

    public User(string userName) : base(userName)
    {
    }
}
