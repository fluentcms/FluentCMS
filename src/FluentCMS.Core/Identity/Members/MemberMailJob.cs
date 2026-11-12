using FluentCMS.Jobs;

namespace FluentCMS.Core.Identity.Members;

// What a visitor asked for with an address: the verification mail again, or a reset link (AccountMail).
internal sealed record MemberMailRequest(string Kind, string Email);

// Sends what resend-verification and forgot-password asked for, to an active member of the address and to nobody else.
internal sealed class MemberMailJob(MemberManager members, MemberMail mail) : IJob<MemberMailRequest>
{
    public const string Type = "members.send_mail";

    public const string Verification = "verification";

    public const string PasswordReset = "password_reset";

    public async Task ExecuteAsync(MemberMailRequest payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(jobContext);

        if (await members.FindByEmailAsync(payload.Email) is not { DeactivatedAt: null } member)
        {
            return;
        }

        var cancellationToken = jobContext.CancellationToken;
        if (payload.Kind == PasswordReset)
        {
            await mail.SendPasswordResetAsync(member, await members.GeneratePasswordResetTokenAsync(member), cancellationToken);
        }
        else if (payload.Kind == Verification && !member.EmailConfirmed)
        {
            // A member a provider made is told which provider its link lets in.
            var token = await members.GenerateEmailConfirmationTokenAsync(member);
            if ((await members.GetLoginsAsync(member)).FirstOrDefault() is { } login)
            {
                await mail.SendProviderVerificationAsync(member, login.ProviderDisplayName ?? login.LoginProvider, token, MemberMailSlot.Requested, cancellationToken);
            }
            else
            {
                await mail.SendVerificationAsync(member, token, MemberMailSlot.Requested, cancellationToken);
            }
        }
    }
}
