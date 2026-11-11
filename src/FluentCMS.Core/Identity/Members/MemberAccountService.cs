using FluentCMS.Content;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.Members;

// The member as /me shows it to itself.
internal sealed record MemberProfile(
    Guid Id,
    string Email,
    bool EmailVerified,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Groups);

// What a visitor and a member do with an account of the member pool, for /api/members/v1 and the hosted pages alike.
// Registering, asking for the verification mail again and asking for a reset answer the same whether or not the
// address is a member's: they return nothing, and what they did is in the mail or in the log. What is wrong with the
// request itself, such as an address that is none or a password the policy refuses, is told, since it says nothing of
// who is a member. A reset and a new password end every session of the member.
internal sealed class MemberAccountService(
    MemberManager members,
    MemberMail mail,
    MemberSessions sessions,
    MemberOptions options,
    SystemDbContext context,
    IOperationContextAccessor accessor,
    TimeProvider time)
{
    public async Task RegisterAsync(string email, string password, string? displayName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        if (!options.AllowRegistration)
        {
            throw new MemberRegistrationClosedException();
        }

        var address = email.Trim();
        var name = MemberViews.DisplayName(displayName);
        await RequireValidAsync(address, password, name);

        var existing = await members.FindByEmailAsync(address);
        if (existing is null)
        {
            var member = new CmsMember { Email = address, DisplayName = name };
            using (MemberOperation.Begin(accessor, member, time))
            {
                // Another registration of the address may win the race, by the validator's lookup or by the unique
                // index; this one is then answered like any other and sends nothing.
                try
                {
                    var created = await members.CreateAsync(member, password);
                    if (created.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
                    {
                        return;
                    }

                    MemberViews.Check(created);
                }
                catch (DbUpdateException)
                {
                    return;
                }
            }

            await mail.SendVerificationAsync(member, await members.GenerateEmailConfirmationTokenAsync(member), cancellationToken);
            return;
        }

        if (existing.EmailConfirmed || existing.DeactivatedAt is not null)
        {
            // As long as hashing a new member's password, and a mail goes out as it does for one.
            _ = members.PasswordHasher.HashPassword(existing, password);
            await mail.SendAlreadyRegisteredAsync(existing, cancellationToken);
            return;
        }

        // Nobody has shown that the address is theirs yet, so the password of the last registration is the one the next
        // verification mail stands for. Whoever registered before cannot sign in with theirs once the owner verifies.
        using (MemberOperation.Begin(accessor, existing, time))
        {
            MemberViews.Check(await members.ReplacePendingAsync(existing, password, name));
        }

        await mail.SendVerificationAsync(existing, await members.GenerateEmailConfirmationTokenAsync(existing), cancellationToken);
    }

    public async Task VerifyEmailAsync(string email, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);

        var member = await ActiveAsync(email) ?? throw MemberViews.InvalidToken();
        using (MemberOperation.Begin(accessor, member, time))
        {
            if (!(await members.ConfirmEmailAsync(member, token)).Succeeded)
            {
                throw MemberViews.InvalidToken();
            }
        }
    }

    public async Task ResendVerificationAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (await ActiveAsync(email) is { EmailConfirmed: false } member)
        {
            await mail.SendVerificationAsync(member, await members.GenerateEmailConfirmationTokenAsync(member), cancellationToken);
        }
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (await ActiveAsync(email) is { } member)
        {
            await mail.SendPasswordResetAsync(member, await members.GeneratePasswordResetTokenAsync(member), cancellationToken);
        }
    }

    public async Task ResetPasswordAsync(string email, string token, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(password);

        var member = await ActiveAsync(email) ?? throw MemberViews.InvalidToken();
        using (MemberOperation.Begin(accessor, member, time))
        {
            MemberViews.Check(await members.ResetPasswordAsync(member, token, password));
        }

        await sessions.RevokeAllAsync(member.Id, cancellationToken);
    }

    public async Task<MemberProfile> MeAsync(CancellationToken cancellationToken) =>
        await ProfileAsync(await CallerAsync(), cancellationToken);

    public async Task<MemberProfile> UpdateMeAsync(string? displayName, CancellationToken cancellationToken)
    {
        var member = await CallerAsync();
        member.DisplayName = MemberViews.DisplayName(displayName);
        MemberViews.Check(await members.UpdateAsync(member));
        return await ProfileAsync(member, cancellationToken);
    }

    // A wrong current password counts towards the lockout, as a wrong one at sign-in does, so that a token that leaked
    // is no way to guess it.
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var member = await CallerAsync();
        if (await members.IsLockedOutAsync(member))
        {
            throw WrongPassword();
        }

        var changed = await members.ChangePasswordAsync(member, currentPassword, newPassword);
        if (changed.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            await members.AccessFailedAsync(member);
            throw WrongPassword();
        }

        MemberViews.Check(changed);
        await sessions.RevokeAllAsync(member.Id, cancellationToken);
    }

    // What is wrong with the request whoever sends it, checked before anything is looked up.
    private async Task RequireValidAsync(string address, string password, string? displayName)
    {
        List<ValidationError> errors = [];
        if (!MemberValidator.IsEmail(address))
        {
            errors.Add(new ValidationError("email", MemberErrorCodes.InvalidEmail, "The email address is not valid."));
        }

        if (!MemberValidator.IsDisplayName(displayName))
        {
            errors.Add(new ValidationError("displayName", MemberErrorCodes.InvalidMember, $"A display name has at most {MemberValidator.MaxDisplayNameLength} characters and no control character."));
        }

        errors.AddRange((await members.ValidatePasswordAsync(password)).Errors
            .Select(error => new ValidationError("password", MemberErrorCodes.InvalidPassword, error.Description)));

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task<CmsMember?> ActiveAsync(string email) =>
        MemberValidator.IsEmail(email.Trim()) && await members.FindByEmailAsync(email.Trim()) is { DeactivatedAt: null } member ? member : null;

    // The member of the token. Its access tokens are revoked when it is deactivated or deleted, so one that is still
    // accepted and names no active member is refused here all the same.
    private async Task<CmsMember> CallerAsync()
    {
        var principal = accessor.Current?.Principal ?? throw new InvalidOperationException("The member is that of an operation, and none is running.");
        return principal.Kind == PrincipalKind.Member && await members.FindByIdAsync(principal.Id.ToString("D")) is { DeactivatedAt: null } member
            ? member
            : throw new MemberAccessDeniedException("This needs the access token of a member.");
    }

    private async Task<MemberProfile> ProfileAsync(CmsMember member, CancellationToken cancellationToken)
    {
        var groups = (await MemberViews.GroupsOfAsync(context, [member.Id], cancellationToken))[member.Id];
        return new MemberProfile(member.Id, member.Email ?? "", member.EmailConfirmed, member.DisplayName, member.CreatedAt, member.LastLoginAt, [.. groups.Select(group => group.Name)]);
    }

    private static ValidationException WrongPassword() =>
        new([new ValidationError("currentPassword", MemberErrorCodes.WrongPassword, "The current password is not right, or too many wrong ones were sent; wait a few minutes before you try again.")]);
}
