using System.Globalization;
using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

internal static class MemberAuthentication
{
    // The sign-in cookie of the member pages, apart from the Identity cookie of the admin users, so that a member session
    // and an admin session in one browser neither sign each other in nor out.
    public const string CookieScheme = "FluentCms.Members";

    public const string CookieName = ".FluentCms.Members";

    // The form field by which the member logout page says that it asked. /connect/logout takes a confirmation only from
    // the page of the pool it signs out, and neither logout page passes the field on from a link.
    public const string LogoutPageField = "fcms_pool";
    public const string LogoutPageMark = "members";
}

// The sign-in manager of the member pool: the member cookie in place of Identity's application cookie, an address in
// place of a user name, and a member who is deactivated or has not verified its address refused whatever its password.
// Every sign-in raises MemberLoginSucceededEvent or MemberLoginFailedEvent with the way in as its method, which is how a
// provider's sign-in names itself.
internal sealed class MemberSignInManager : SignInManager<CmsMember>
{
    private readonly MemberManager _members;
    private readonly MemberSignInEvents _events;
    private readonly TimeProvider _time;

    public MemberSignInManager(
        MemberManager members,
        IHttpContextAccessor contextAccessor,
        MemberClaimsPrincipalFactory claimsFactory,
        MemberIdentityOptions options,
        ILogger<SignInManager<CmsMember>> logger,
        IAuthenticationSchemeProvider schemes,
        MemberSignInEvents events,
        TimeProvider time)
        : base(members, contextAccessor, claimsFactory, options, logger, schemes, new DefaultUserConfirmation<CmsMember>())
    {
        _members = members;
        _events = events;
        _time = time;
        AuthenticationScheme = MemberAuthentication.CookieScheme;
    }

    public override async Task<bool> CanSignInAsync(CmsMember user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.DeactivatedAt is null && await base.CanSignInAsync(user);
    }

    // Active, verified and not locked out; CanSignInAsync does not look at lockout, which the password check alone applies.
    public async Task<bool> MaySignInAsync(CmsMember user) =>
        await CanSignInAsync(user) && !await _members.IsLockedOutAsync(user);

    // The member of the sign-in cookie, if the cookie is still good: its security stamp holds and the member may sign in.
    public async Task<(CmsMember? Member, AuthenticateResult Cookie)> SignedInAsync()
    {
        var cookie = await Context.AuthenticateAsync(MemberAuthentication.CookieScheme);
        var member = cookie.Succeeded ? await ValidateSecurityStampAsync(cookie.Principal) : null;
        return (member is not null && await MaySignInAsync(member) ? member : null, cookie);
    }

    // The hosted login page: the name is the member's address.
    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        var member = await _members.FindByEmailAsync(userName);
        if (member is null)
        {
            await RefuseUnknownAsync(password, MemberLoginMethods.Password);
            return SignInResult.Failed;
        }

        var result = await base.PasswordSignInAsync(member, password, isPersistent, lockoutOnFailure);
        await RecordAsync(member, MemberLoginMethods.Password, result);
        return result;
    }

    // The direct login grant: the password is checked, with lockout, and no cookie is made. Null is a refusal, whose
    // reason the caller never learns.
    public async Task<CmsMember?> DirectLoginAsync(string email, string password)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        var member = await _members.FindByEmailAsync(email);
        if (member is null)
        {
            await RefuseUnknownAsync(password, MemberLoginMethods.DirectLogin);
            return null;
        }

        var result = await CheckPasswordSignInAsync(member, password, lockoutOnFailure: true);
        await RecordAsync(member, MemberLoginMethods.DirectLogin, result);
        return result.Succeeded ? member : null;
    }

    // A member who may not sign in is refused before Identity checks the password, so the hash runs anyway: the answer
    // takes as long as for a wrong password or an unknown address.
    public override async Task<SignInResult> CheckPasswordSignInAsync(CmsMember user, string password, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        if (await PreSignInCheck(user) is { } refused)
        {
            _ = _members.PasswordHasher.HashPassword(user, password);
            return refused;
        }

        return await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
    }

    // Every sign-in that makes a new cookie comes here. The time of the sign-in is kept in the cookie's properties, as
    // for an admin user: prompt=login, max_age and the absolute session lifetime read it.
    public override Task SignInWithClaimsAsync(CmsMember user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var properties = authenticationProperties ?? new AuthenticationProperties();
        if (!properties.Items.ContainsKey(CmsSignInManager.SignedInAtItem))
        {
            properties.Items[CmsSignInManager.SignedInAtItem] = _time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        return base.SignInWithClaimsAsync(user, properties, additionalClaims);
    }

    // The member cookie alone. Identity's sign-out also ends its external and two-factor cookies, which are the admin's.
    public override Task SignOutAsync() => Context.SignOutAsync(MemberAuthentication.CookieScheme);

    // A sign-in that some other way than a password completed, such as an external provider's: the method names it.
    public async Task RecordAsync(CmsMember member, string method, SignInResult result)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Succeeded)
        {
            await _members.TouchLastLoginAsync(member);
            await _events.SucceededAsync(Context, member, method);
            return;
        }

        var reason = result.IsLockedOut ? LoginFailureReasons.LockedOut
            : result.IsNotAllowed ? LoginFailureReasons.NotAllowed
            : LoginFailureReasons.InvalidCredentials;
        await _events.FailedAsync(Context, member, method, reason);
    }

    // As long as a password check, so that the time of the answer does not tell whether the address is a member's.
    private async Task RefuseUnknownAsync(string password, string method)
    {
        _ = _members.PasswordHasher.HashPassword(new CmsMember(), password);
        await _events.FailedAsync(Context, null, method, LoginFailureReasons.UnknownUser);
    }
}

// The principal of the member cookie: the member's id, its address as the name and its security stamp, under the
// member scheme, so that nothing takes it for an admin user's.
internal sealed class MemberClaimsPrincipalFactory(MemberIdentityOptions options) : IUserClaimsPrincipalFactory<CmsMember>
{
    public Task<ClaimsPrincipal> CreateAsync(CmsMember user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var claims = options.Value.ClaimsIdentity;
        var identity = new ClaimsIdentity(MemberAuthentication.CookieScheme, claims.UserNameClaimType, claims.RoleClaimType);
        identity.AddClaim(new Claim(claims.UserIdClaimType, user.Id.ToString("D")));
        identity.AddClaim(new Claim(claims.UserNameClaimType, user.Email ?? ""));
        identity.AddClaim(new Claim(claims.SecurityStampClaimType, user.SecurityStamp ?? ""));
        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}

// The sign-in events of the member pool, which the hosted pages and the token endpoint raise outside any operation: a
// success is the member's, a failure is the anonymous principal's, both of no site. They are written even when the
// client goes away, so that a dropped connection does not hide a failure.
internal sealed class MemberSignInEvents(IdentityChanges changes, TimeProvider time)
{
    public Task SucceededAsync(HttpContext context, CmsMember member, string method) =>
        changes.RecordAsync(
            new MemberLoginSucceededEvent(member.Id, member.Email ?? "", method, Address(context))
            {
                Source = Source(context, member.Id, member.Email ?? "", PrincipalKind.Member),
            },
            CancellationToken.None);

    public Task FailedAsync(HttpContext context, CmsMember? member, string method, string reason) =>
        changes.RecordAsync(
            new MemberLoginFailedEvent(member?.Id, member?.Email, method, reason, Address(context))
            {
                Source = Source(context, Principal.Anonymous.Id, Principal.Anonymous.Name, PrincipalKind.Anonymous),
            },
            CancellationToken.None);

    private EventSource Source(HttpContext context, Guid actorId, string actorName, PrincipalKind kind) =>
        new(time.GetUtcNow(), Guid.Empty, actorId, actorName, kind, Origin.Api, System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);

    private static string? Address(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
            ? (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()
            : null;
}
