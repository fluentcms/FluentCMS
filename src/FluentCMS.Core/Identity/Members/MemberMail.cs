using FluentCMS.Core.DependencyInjection;
using FluentCMS.Providers;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

// The mail of the member pool: the verification link, the link that sets or resets a password, and the note that an
// address is registered already. A link goes to the front end's page when cms.Members.Links names one, and to the hosted
// page on the public origin otherwise; it is never built from a request, so without either no mail goes out. An address
// gets one mail a minute, and a mail that is held back or fails is logged and never told to the caller, whose answer is
// the same either way.
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

    public Task<bool> SendVerificationAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, options.Links.VerifyEmail, HostedPagePaths.MemberVerifyEmail, token, link => new EmailMessage(
            member.Email!,
            "Verify your email address",
            $"Someone registered an account with this email address. If it was you, verify the address here:\n\n{link}\n\nThe link can be used once and expires at {Expiry(options.EmailVerificationLifetime)} UTC. If you did not register, ignore this email; the account is removed if the address is not verified."), cancellationToken);

    public Task<bool> SendPasswordResetAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, options.Links.ResetPassword, HostedPagePaths.MemberResetPassword, token, link => new EmailMessage(
            member.Email!,
            "Reset your password",
            $"Someone asked to reset the password of the account with this email address. Choose a new password here:\n\n{link}\n\nThe link can be used once and expires at {Expiry(options.PasswordResetLifetime)} UTC. If you did not ask for it, ignore this email; your password stays as it is."), cancellationToken);

    // For a member an administrator created, who has no password yet.
    public Task<bool> SendSetPasswordAsync(CmsMember member, string token, CancellationToken cancellationToken) =>
        SendAsync(member, options.Links.ResetPassword, HostedPagePaths.MemberResetPassword, token, link => new EmailMessage(
            member.Email!,
            "Choose a password for your account",
            $"An account was created for this email address. Choose a password here to start using it:\n\n{link}\n\nThe link can be used once and expires at {Expiry(options.PasswordResetLifetime)} UTC. After that, ask for a new link with the forgotten password page."), cancellationToken);

    // No link: the member has an account and a password already, and the mail says how to get back in.
    public Task<bool> SendAlreadyRegisteredAsync(CmsMember member, CancellationToken cancellationToken) =>
        DeliverAsync(member, new EmailMessage(
            member.Email!,
            "You already have an account",
            "Someone tried to register an account with this email address, which has an account already. If it was you, sign in with your password, or reset it with the forgotten password page. If it was not you, ignore this email; nothing has changed."), cancellationToken);

    private async Task<bool> SendAsync(CmsMember member, string? template, string hostedPage, string token, Func<string, EmailMessage> message, CancellationToken cancellationToken)
    {
        if (Template(template, hostedPage) is not { } resolved)
        {
            MemberLog.NoMailLink(logger, member.Id);
            return false;
        }

        var link = resolved
            .Replace(MemberLinkOptions.EmailPlaceholder, Uri.EscapeDataString(member.Email!), StringComparison.Ordinal)
            .Replace(MemberLinkOptions.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);
        return await DeliverAsync(member, message(link), cancellationToken);
    }

    private async Task<bool> DeliverAsync(CmsMember member, EmailMessage message, CancellationToken cancellationToken)
    {
        if (!limiter.TryAcquire(member.NormalizedEmail ?? ""))
        {
            MemberLog.MailHeldBack(logger, member.Id);
            return false;
        }

        try
        {
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

    private string Expiry(TimeSpan lifetime) => (time.GetUtcNow() + lifetime).UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

// One mail a minute to an address, counted in memory on each instance, as the limits of the APIs are. It is asked only
// when a mail is about to go, so an address that gets none holds no place here.
internal sealed class MemberMailLimiter(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const int SweepAbove = 4096;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _sent = new(StringComparer.Ordinal);

    public bool TryAcquire(string address)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_sent.Count > SweepAbove)
            {
                foreach (var idle in _sent.Where(entry => now - entry.Value >= Window).Select(entry => entry.Key).ToList())
                {
                    _sent.Remove(idle);
                }
            }

            if (_sent.TryGetValue(address, out var last) && now - last < Window)
            {
                return false;
            }

            _sent[address] = now;
            return true;
        }
    }
}
