using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity.Members;

// A row of cms_member: a visitor's account in the member pool, apart from the admin users. The pool is one for the
// instance, so a member belongs to no site, and its email address is its sign-in name. What Identity's user has and a
// member has not is fixed here and has no column: no user name of its own, no phone number, no second factor, and
// lockout that is always on.
public sealed class CmsMember : IdentityUser<Guid>
{
    public CmsMember()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Set by MemberManager.DeactivateAsync. A deactivated member cannot sign in, and its tokens are refused.
    public DateTimeOffset? DeactivatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public override string? UserName
    {
        get => Email;
        set { }
    }

    public override string? NormalizedUserName
    {
        get => NormalizedEmail;
        set { }
    }

    public override bool LockoutEnabled
    {
        get => true;
        set { }
    }

    public override bool TwoFactorEnabled
    {
        get => false;
        set { }
    }

    public override string? PhoneNumber
    {
        get => null;
        set { }
    }

    public override bool PhoneNumberConfirmed
    {
        get => false;
        set { }
    }
}

// A row of cms_member_login: an external login of a member, as Identity's store keeps it.
internal sealed class CmsMemberLogin : IdentityUserLogin<Guid>;

// A row of cms_member_token: a token Identity's store keeps for a member.
internal sealed class CmsMemberToken : IdentityUserToken<Guid>;

// Identity's store asks for a claim type. Members have no claims table, so this one is never mapped and never stored.
internal sealed class CmsMemberClaim : IdentityUserClaim<Guid>;
