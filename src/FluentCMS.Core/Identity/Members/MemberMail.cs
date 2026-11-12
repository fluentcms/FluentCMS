using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

// The mail of the member pool: the verification link, also for a member a provider made, the link that sets or resets a
// password, and the note that an address is registered already. A link goes to the front end's page when cms.Members.Links names one, and to the hosted
// page on the public origin otherwise; it is never built from a request, so without either no mail goes out. An address
// gets one mail a minute of each slot, and a mail that is held back or fails is logged and never told to the caller, whose
// answer is the same either way. A link to a member that is not verified expires no later than the job may remove it.
internal sealed class MemberMail(
    IEmailSender email,
    MemberOptions options,
    PublicOrigin origin,
    MemberMailLimiter limiter,
    TimeProvider time,
    ILogger<MemberMail> logger)
{
    // Whether a link can be built at all, which an administrator who creates a member is told before the member is made.
    public bool CanLinkToPasswordReset => Template(options.Links.ResetPassword, HostedPagePaths.MemberResetPassword) is not null;

    public Task<bool> SendVerificationAsync(CmsMember member, string token, MemberMailSlot slot, CancellationToken cancellationToken) =>
        SendAsync(member, slot, options.Links.VerifyEmail, HostedPagePaths.MemberVerifyEmail, token, options.EmailVerificationLifetime, (link, expiry) => new EmailMessage(
            member.Email!,
            "Verify your email address",
            $"Someone registered an account with this email address. If it was you, verify the address here:\n\n{link}\n\nThe link can be used once and expires at {expiry} UTC. If you did not register, ignore this email; the account is removed if the address is not verified."), cancellationToken);

    // For a member a provider made without saying that its address is verified. The mail names the provider, so that
    // the owner of the mailbox knows what the link allows: the account at the provider signs in as this member.
    public Task<bool> SendProviderVerificationAsync(CmsMember member, string provider, string token, MemberMailSlot slot, CancellationToken cancellationToken) =>
        SendAsync(member, slot, options.Links.VerifyEmail, HostedPagePaths.MemberVerifyEmail, token, options.EmailVerificationLifetime, (link, expiry) => new EmailMessage(
            member.Email!,
            "Verify your email address",
            $"Someone signed in with {provider} and gave this email address. If it was you, verify the address here:\n\n{link}\n\nThe link can be used once and expires at {expiry} UTC. Once it is verified, that {provider} account signs in to the account of this address. If it was not you, ignore this email and do not use the link; the account is removed if the address is not verified."), cancellationToken);

    public Task<bool> SendPasswordResetAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, MemberMailSlot.Requested, options.Links.ResetPassword, HostedPagePaths.MemberResetPassword, token, options.PasswordResetLifetime, (link, expiry) => new EmailMessage(
            member.Email!,
            "Reset your password",
            $"Someone asked to reset the password of the account with this email address. Choose a new password here:\n\n{link}\n\nThe link can be used once and expires at {expiry} UTC. If you did not ask for it, ignore this email; your password stays as it is."), cancellationToken);

    // For a member an administrator created, who has no password yet.
    public Task<bool> SendSetPasswordAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, MemberMailSlot.Requested, options.Links.ResetPassword, HostedPagePaths.MemberResetPassword, token, options.PasswordResetLifetime, (link, expiry) => new EmailMessage(
            member.Email!,
            "Choose a password for your account",
            $"An account was created for this email address. Choose a password here to start using it:\n\n{link}\n\nThe link can be used once and expires at {expiry} UTC. After that, ask for a new link with the forgotten password page."), cancellationToken);

    // For an address that was registered again before it was verified: no password of a registration was kept.
    public Task<bool> SendChoosePasswordAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, MemberMailSlot.Notice, options.Links.ResetPassword, HostedPagePaths.MemberResetPassword, token, options.PasswordResetLifetime, (link, expiry) => new EmailMessage(
            member.Email!,
            "Choose a password to finish registering",
            $"This email address was registered more than once before it was verified, so no password was kept for it. If it was you, choose your password here, which also verifies the address:\n\n{link}\n\nThe link can be used once and expires at {expiry} UTC. After that, ask for a new link with the forgotten password page. If you did not register, ignore this email; the account is removed if the address is not verified."), cancellationToken);

    // No link: the address has an account already, and the mail says how to get back in.
    public Task<bool> SendAlreadyRegisteredAsync(CmsMember member, CancellationToken cancellationToken) =>
        DeliverAsync(member, MemberMailSlot.Notice, new EmailMessage(
            member.Email!,
            "You already have an account",
            "Someone tried to register an account with this email address, which has an account already. If it was you, sign in with your password, or choose a new one with the forgotten password page. If it was not you, ignore this email; nothing has changed."), cancellationToken);

    private async Task<bool> SendAsync(CmsMember member, MemberMailSlot slot, string? template, string hostedPage, string token, TimeSpan lifetime, Func<string, string, EmailMessage> message, CancellationToken cancellationToken)
    {
        if (Template(template, hostedPage) is not { } resolved)
        {
            MemberLog.NoMailLink(logger, member.Id);
            return false;
        }

        // The token providers refuse a token after the same end, so the time the mail gives is the time the link has.
        var now = time.GetUtcNow();
        var expiresAt = now + lifetime;
        if (UnverifiedMembersJob.RemovableAt(member, options) is { } removable && removable < expiresAt)
        {
            if (removable <= now)
            {
                MemberLog.MailTooLate(logger, member.Id);
                return false;
            }

            expiresAt = removable;
        }

        var link = resolved
            .Replace(MemberLinkOptions.EmailPlaceholder, Uri.EscapeDataString(member.Email!), StringComparison.Ordinal)
            .Replace(MemberLinkOptions.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);
        return await DeliverAsync(member, slot, message(link, expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), cancellationToken);
    }

    // A limit that cannot be read sends nothing, as a send that fails does.
    private async Task<bool> DeliverAsync(CmsMember member, MemberMailSlot slot, EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (!await limiter.TryAcquireAsync(member.NormalizedEmail ?? "", slot, cancellationToken))
            {
                MemberLog.MailHeldBack(logger, member.Id);
                return false;
            }

            await email.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MemberLog.MailFailed(logger, member.Id, exception);
            return false;
        }
    }

    // The front end's template, or the hosted page on the public origin with the same two parameters.
    private string? Template(string? configured, string hostedPage) =>
        !string.IsNullOrWhiteSpace(configured) ? configured
        : origin.Base is { } publicOrigin ? $"{publicOrigin}{hostedPage}?email={MemberLinkOptions.EmailPlaceholder}&token={MemberLinkOptions.TokenPlaceholder}"
        : null;
}

// Which mails of an address share its limit. What its owner asks for, a reset or the verification mail again, and the link
// an administrator sends a new member are Requested; what a registration or a sign-in through a provider sends, which
// anyone who knows the address can cause, is a Notice. So registering an address over and over never holds back the mail
// its owner asks for, and an address gets at most one mail of each a minute.
internal enum MemberMailSlot
{
    Requested,
    Notice,
}

// One mail a minute to an address in each slot. It is kept in the distributed cache, which every instance sees once the
// host registers a shared one, and decided under one distributed lock, so that of two instances that ask at once only one
// sends. The key holds a hash of the address, never the address, and the entry expires with the window. It is asked only
// when a mail is about to go, so an address that gets none holds no place here.
internal sealed class MemberMailLimiter(IDistributedCache cache, IDistributedLock locks, TimeProvider time)
{
    public const string LockName = "members.mail_limit";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);

    // The value is the time of the last mail, read against the clock of the instance, so a cache that keeps an entry a
    // little longer than asked holds nothing back for longer.
    public async Task<bool> TryAcquireAsync(string address, MemberMailSlot slot, CancellationToken cancellationToken)
    {
        await using var held = await locks.TryAcquireAsync(LockName, LockWait, cancellationToken);
        if (held is null)
        {
            return false;
        }

        var key = Key(address, slot);
        var now = time.GetUtcNow();
        if (await cache.GetAsync(key, cancellationToken) is { Length: sizeof(long) } last
            && now - new DateTimeOffset(BinaryPrimitives.ReadInt64LittleEndian(last), TimeSpan.Zero) < Window)
        {
            return false;
        }

        var sent = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(sent, now.UtcTicks);
        await cache.SetAsync(key, sent, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Window }, cancellationToken);
        return true;
    }

    private static string Key(string address, MemberMailSlot slot) =>
        $"fluentcms:members:mail:{(slot == MemberMailSlot.Requested ? "requested" : "notice")}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address)))}";
}
