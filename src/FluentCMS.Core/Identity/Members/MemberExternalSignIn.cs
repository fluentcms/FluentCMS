using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

// What a provider of cms.Members.Providers said about whoever signed in at it, read from the external cookie of the
// member pool. LinkFor is the member that asked to link the login on the hosted pages, when one did.
internal sealed record MemberExternalLogin(string Provider, string ProviderName, string Key, string? Email, bool EmailVerified, string? DisplayName, Guid? LinkFor);

internal enum MemberExternalOutcome
{
    SignedIn,

    // Nobody is signed in yet: a mail went to the address, which the provider did not say is verified.
    Pending,

    Refused,
}

// The sign-in of the member external login page. A member linked to the login signs in. Anyone else gets a member only
// while registration is open, named by the provider's address and display name, with no password. The address of that
// member counts as verified only when the provider says so; otherwise the member gets the verification mail and signs
// in once the address is verified. An address that belongs to a member is never taken over: the sign-in is refused,
// unless the provider links verified addresses and says this one is verified, and the member may sign in now. A
// sign-in with an address the provider did not verify is answered the same whether or not the address is a member's.
internal sealed class MemberExternalSignIn(
    MemberOptions options,
    MemberManager members,
    MemberSignInManager signIn,
    MemberSignInEvents events,
    MemberMail mail,
    MemberLogins logins,
    IdentityChanges changes,
    IOperationContextAccessor accessor,
    TimeProvider time,
    ILogger<MemberExternalSignIn> logger)
{
    public const string EmailVerifiedClaim = "email_verified";

    // The length of the provider key column of cms_member_login.
    private const int MaxKeyLength = 128;

    // The external login of the request, if a provider of the member pool signed one in. A cookie that names no
    // provider of cms.Members, as one made for another pool would, is no login.
    public async Task<MemberExternalLogin?> ReadAsync()
    {
        var cookie = await signIn.Context.AuthenticateAsync(MemberProviders.ExternalScheme);
        if (!cookie.Succeeded
            || !cookie.Properties.Items.TryGetValue(MemberProviders.ProviderItem, out var name)
            || name is null
            || !options.Providers.TryGetValue(name, out var provider))
        {
            return null;
        }

        var principal = cookie.Principal;
        var key = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return null;
        }

        var kind = provider.Kind;
        var email = principal.FindFirstValue(kind == MemberProviderKind.Oidc ? "email" : ClaimTypes.Email)?.Trim();
        var displayName = kind switch
        {
            MemberProviderKind.Oidc => principal.FindFirstValue("name"),
            MemberProviderKind.GitHub => principal.FindFirstValue(MemberProviders.GitHubNameClaim) ?? principal.FindFirstValue(ClaimTypes.Name),
            MemberProviderKind.Apple => null,
            _ => principal.FindFirstValue(ClaimTypes.Name),
        };

        // Whether the address is verified is the provider's word. Microsoft never says so. The GitHub handler here takes
        // only the primary address that GitHub verified, so an address from it is one.
        var verified = kind switch
        {
            MemberProviderKind.Microsoft => false,
            MemberProviderKind.GitHub => true,
            _ => string.Equals(principal.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase),
        };

        var linkFor = cookie.Properties.Items.TryGetValue(MemberProviders.LinkItem, out var member) && Guid.TryParse(member, out var memberId) ? memberId : (Guid?)null;
        var address = MemberValidator.IsEmail(email) ? email : null;
        return new MemberExternalLogin(
            name,
            provider.Name(name),
            key,
            address,
            address is not null && verified,
            MemberViews.DisplayName(displayName) is { } shown && MemberValidator.IsDisplayName(shown) ? shown : null,
            linkFor);
    }

    // The external cookie has done its work once the page has decided, whatever it decided.
    public Task ForgetAsync() => signIn.Context.SignOutAsync(MemberProviders.ExternalScheme);

    public async Task<MemberExternalOutcome> SignInAsync(MemberExternalLogin login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);

        if (await members.FindByLoginAsync(login.Provider, login.Key) is { } linked)
        {
            return await CompleteAsync(linked, login, cancellationToken);
        }

        var existing = login.Email is null ? null : await members.FindByEmailAsync(login.Email);
        if (existing is not null)
        {
            if (login.EmailVerified && options.Providers[login.Provider].LinkVerifiedEmail)
            {
                return await LinkExistingAsync(existing, login)
                    ? await CompleteAsync(existing, login, cancellationToken)
                    : MemberExternalOutcome.Refused;
            }

            await RefusedAsync(login, MemberErrorCodes.ProviderEmailTaken, "the address belongs to a member who is not linked to this provider.", LoginFailureReasons.NotAllowed);
            if (login.EmailVerified || !options.AllowRegistration)
            {
                return MemberExternalOutcome.Refused;
            }

            // Nobody has shown that the address is theirs, so the answer is the one a new address gets, and the mail
            // tells its owner that it has an account already.
            await mail.SendAlreadyRegisteredAsync(existing, cancellationToken);
            return MemberExternalOutcome.Pending;
        }

        if (!options.AllowRegistration)
        {
            await RefusedAsync(login, MemberErrorCodes.ProviderRegistrationClosed, "no member is linked to the login, and registration is not open.", LoginFailureReasons.UnknownUser);
            return MemberExternalOutcome.Refused;
        }

        if (login.Email is null)
        {
            await RefusedAsync(login, MemberErrorCodes.ProviderNoEmail, "the provider gave no email address that can name a member.", LoginFailureReasons.NotAllowed);
            return MemberExternalOutcome.Refused;
        }

        if (await CreateAsync(login, cancellationToken) is not { } member)
        {
            return MemberExternalOutcome.Refused;
        }

        return await CompleteAsync(member, login, cancellationToken);
    }

    // A signed-in member links the login to itself. A login belongs to one member, and a member has one login of a
    // provider, which is what removing a login by its provider counts on.
    public async Task<IdentityResult> LinkAsync(CmsMember member, MemberExternalLogin login)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(login);

        if (await members.FindByLoginAsync(login.Provider, login.Key) is { } owner)
        {
            return Taken(owner.Id == member.Id
                ? $"This {login.ProviderName} account is linked to your account already."
                : $"This {login.ProviderName} account is linked to another account here.");
        }

        if ((await members.GetLoginsAsync(member)).Any(held => string.Equals(held.LoginProvider, login.Provider, StringComparison.OrdinalIgnoreCase)))
        {
            return Taken($"Your account has a {login.ProviderName} sign-in already. Remove it before you link another.");
        }

        using (MemberOperation.Begin(accessor, member, time))
        {
            try
            {
                return await members.AddLoginAsync(member, new UserLoginInfo(login.Provider, login.Key, login.ProviderName));
            }
            catch (DbUpdateException)
            {
                // Linked to a member by another request at the same time.
                return Taken($"This {login.ProviderName} account is linked to another account here.");
            }
        }
    }

    // The member removes a login on the hosted pages. Removing it gives a new security stamp, which ends every sign-in
    // of the member; the cookie of the browser that asked is made again, so that the member stays on the page.
    public async Task UnlinkAsync(CmsMember member, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(provider);

        using (MemberOperation.Begin(accessor, member, time))
        {
            await logins.RemoveAsync(member, provider, cancellationToken);
        }

        await signIn.RefreshSignInAsync(member);
    }

    private async Task<MemberExternalOutcome> CompleteAsync(CmsMember member, MemberExternalLogin login, CancellationToken cancellationToken)
    {
        if (await signIn.MaySignInAsync(member))
        {
            await signIn.SignInWithClaimsAsync(member, isPersistent: false, [new Claim(ClaimTypes.AuthenticationMethod, login.Provider)]);
            await signIn.RecordAsync(member, login.Provider, SignInResult.Success);
            return MemberExternalOutcome.SignedIn;
        }

        var lockedOut = await members.IsLockedOutAsync(member);
        await events.FailedAsync(signIn.Context, member, login.Provider, lockedOut ? LoginFailureReasons.LockedOut : LoginFailureReasons.NotAllowed);
        if (member.DeactivatedAt is not null || lockedOut || member.EmailConfirmed)
        {
            return MemberExternalOutcome.Refused;
        }

        // The provider made this member and did not say its address is verified: the mail goes, again if need be.
        await mail.SendProviderVerificationAsync(member, login.ProviderName, await members.GenerateEmailConfirmationTokenAsync(member), cancellationToken);
        return MemberExternalOutcome.Pending;
    }

    // Only a member who may sign in now, and who has no other login of the provider, so that a second account at the
    // provider does not join the first. A member whose address is not verified is not linked: nobody has shown that
    // its password, if it has one, is the choice of the mailbox's owner.
    private async Task<bool> LinkExistingAsync(CmsMember member, MemberExternalLogin login)
    {
        if (!await signIn.MaySignInAsync(member))
        {
            await RefusedAsync(login, MemberErrorCodes.ProviderEmailTaken, "the address belongs to a member who cannot sign in now.", LoginFailureReasons.NotAllowed);
            return false;
        }

        var linked = await LinkAsync(member, login);
        if (!linked.Succeeded)
        {
            await RefusedAsync(login, MemberErrorCodes.ProviderEmailTaken, "the address belongs to a member linked to another account of this provider: " + Codes(linked), LoginFailureReasons.NotAllowed);
            return false;
        }

        MemberLog.ProviderMemberLinked(logger, member.Id, login.Provider);
        return true;
    }

    // The member and its login are one change, so that neither is there without the other.
    private async Task<CmsMember?> CreateAsync(MemberExternalLogin login, CancellationToken cancellationToken)
    {
        var member = new CmsMember { Email = login.Email, DisplayName = login.DisplayName, EmailConfirmed = login.EmailVerified };
        IdentityResult created;
        using (MemberOperation.Begin(accessor, member, time))
        {
            try
            {
                created = await changes.RunAsync(
                    async () => await members.CreateAsync(member) is { Succeeded: false } failed
                        ? failed
                        : await members.AddLoginAsync(member, new UserLoginInfo(login.Provider, login.Key, login.ProviderName)),
                    cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The address or the login was taken by another request at the same time.
                created = IdentityResult.Failed(members.ErrorDescriber.ConcurrencyFailure());
            }
        }

        if (!created.Succeeded)
        {
            // The codes only: a description can hold the address.
            await RefusedAsync(login, MemberErrorCodes.ProviderMemberNotCreated, Codes(created), LoginFailureReasons.NotAllowed);
            return null;
        }

        MemberLog.ProviderMemberCreated(logger, member.Id, login.Provider, member.EmailConfirmed);
        return member;
    }

    // A refusal that names no member: whoever signed in at the provider is not shown to be one.
    private async Task RefusedAsync(MemberExternalLogin login, string code, string reason, string failure)
    {
        MemberLog.ProviderSignInRefused(logger, code, login.Provider, reason);
        await events.FailedAsync(signIn.Context, null, login.Provider, failure);
    }

    private static IdentityResult Taken(string description) =>
        IdentityResult.Failed(new IdentityError { Code = MemberErrorCodes.LoginTaken, Description = description });

    private static string Codes(IdentityResult result) => string.Join(", ", result.Errors.Select(error => error.Code));
}
