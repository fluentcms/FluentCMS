using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity.Members;

// Identity's user manager for members, on the options of cms.Members and not on those of the admin users, which the
// container's IdentityOptions are. It adds deactivation, and it spends a verification link when it is used.
internal sealed class MemberManager : UserManager<CmsMember>
{
    private readonly TimeProvider _time;

    public MemberManager(
        MemberStore store,
        MemberIdentityOptions options,
        IPasswordHasher<CmsMember> passwordHasher,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<CmsMember>> logger,
        TimeProvider time)
        : base(store, options, passwordHasher, [new MemberValidator()], [new PasswordValidator<CmsMember>(errors)], keyNormalizer, errors, services, logger)
    {
        _time = time;
    }

    private MemberStore MemberStore => (MemberStore)Store;

    // The policy of cms.Members on a password, before any member exists to set it on.
    public async Task<IdentityResult> ValidatePasswordAsync(string password)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(password);

        List<IdentityError> errors = [];
        foreach (var validator in PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(this, new CmsMember(), password)).Errors);
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    // Identity's confirmation keeps the security stamp, so the link would verify again. A new stamp spends it, in the save
    // that verifies the address.
    public override async Task<IdentityResult> ConfirmEmailAsync(CmsMember user, string token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(token);

        if (user.EmailConfirmed || !await VerifyUserTokenAsync(user, Options.Tokens.EmailConfirmationTokenProvider, ConfirmEmailTokenPurpose, token))
        {
            return IdentityResult.Failed(ErrorDescriber.InvalidToken());
        }

        user.EmailConfirmed = true;
        var result = await UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            user.EmailConfirmed = false;
        }

        return result;
    }

    // The link of a reset reached the member's mailbox, which is what a verification link shows, so a reset verifies the
    // address too. That is how a member an administrator created, who has no password yet, gets both.
    public override async Task<IdentityResult> ResetPasswordAsync(CmsMember user, string token, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);

        var verified = user.EmailConfirmed;
        user.EmailConfirmed = true;
        var result = await base.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            user.EmailConfirmed = verified;
        }

        return result;
    }

    // An address that is registered and not verified was registered again, and nobody has shown yet that it is theirs:
    // the password of neither registration is kept, nor an external login that a provider which does not verify
    // addresses brought, so whoever owns the mailbox chooses the password with a mailed link. One new security stamp
    // ends the links sent so far, and the member counts as made now, so that the job that removes members who never
    // verified leaves the new link its time. It happens once: a member with neither is not cleared again.
    public async Task<IdentityResult> ClearPendingAsync(CmsMember user, IList<UserLoginInfo> logins)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(logins);

        if (user.EmailConfirmed || (user.PasswordHash is null && logins.Count == 0))
        {
            throw new InvalidOperationException("Only a member whose address is not verified, with a password or an external login, is cleared.");
        }

        var (hash, createdAt) = (user.PasswordHash, user.CreatedAt);
        user.PasswordHash = null;
        user.CreatedAt = _time.GetUtcNow();
        var result = await RemoveLoginsAsync(user, logins);
        if (!result.Succeeded)
        {
            (user.PasswordHash, user.CreatedAt) = (hash, createdAt);
        }

        return result;
    }

    // The logins go in one save with one new security stamp, which ends the member's codes, refresh tokens and cookie.
    public async Task<IdentityResult> RemoveLoginsAsync(CmsMember user, IEnumerable<UserLoginInfo> logins)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(logins);

        foreach (var login in logins)
        {
            await MemberStore.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey, CancellationToken);
        }

        return await UpdateSecurityStampAsync(user);
    }

    // A new security stamp ends the member's codes and refresh tokens; MemberSessions ends its access tokens.
    public async Task<IdentityResult> DeactivateAsync(CmsMember user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.DeactivatedAt is not null)
        {
            return IdentityResult.Success;
        }

        user.DeactivatedAt = _time.GetUtcNow();
        var result = await UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            user.DeactivatedAt = null;
        }

        return result;
    }

    public async Task<IdentityResult> ReactivateAsync(CmsMember user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.DeactivatedAt is not { } deactivated)
        {
            return IdentityResult.Success;
        }

        user.DeactivatedAt = null;
        var result = await UpdateUserAsync(user);
        if (!result.Succeeded)
        {
            user.DeactivatedAt = deactivated;
        }

        return result;
    }

    public override async Task<bool> CheckPasswordAsync(CmsMember user, string password)
    {
        MemberStore.Rehashing = true;
        try
        {
            return await base.CheckPasswordAsync(user, password);
        }
        finally
        {
            MemberStore.Rehashing = false;
        }
    }

    public Task TouchLastLoginAsync(CmsMember user) => MemberStore.TouchLastLoginAsync(user.Id, CancellationToken);
}

// The IdentityOptions of the member pool: the policy and lockout of cms.Members, sign-in with a verified address only,
// and the two token providers of member mail.
internal sealed class MemberIdentityOptions : IOptions<IdentityOptions>
{
    public MemberIdentityOptions(MemberOptions members)
    {
        members.Apply(Value);
        Value.Tokens.ProviderMap[MemberEmailTokenProvider.ProviderName] = new TokenProviderDescriptor(typeof(MemberEmailTokenProvider));
        Value.Tokens.ProviderMap[MemberPasswordResetTokenProvider.ProviderName] = new TokenProviderDescriptor(typeof(MemberPasswordResetTokenProvider));
        Value.Tokens.EmailConfirmationTokenProvider = MemberEmailTokenProvider.ProviderName;
        Value.Tokens.PasswordResetTokenProvider = MemberPasswordResetTokenProvider.ProviderName;
    }

    public IdentityOptions Value { get; } = new();
}

// An address is what a member is known by: it is there, it is an address, and no other member has it. Identity's own
// validator would also hold the address to the characters of a user name.
internal sealed class MemberValidator : IUserValidator<CmsMember>
{
    public const int MaxEmailLength = 256;
    public const int MaxDisplayNameLength = 128;

    public const string InvalidDisplayName = "InvalidDisplayName";

    public async Task<IdentityResult> ValidateAsync(UserManager<CmsMember> manager, CmsMember user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        List<IdentityError> errors = [];
        if (!IsEmail(user.Email))
        {
            errors.Add(manager.ErrorDescriber.InvalidEmail(user.Email));
        }
        else if (await manager.FindByEmailAsync(user.Email!) is { } owner && owner.Id != user.Id)
        {
            errors.Add(manager.ErrorDescriber.DuplicateEmail(user.Email!));
        }

        if (!IsDisplayName(user.DisplayName))
        {
            errors.Add(new IdentityError { Code = InvalidDisplayName, Description = $"A display name has at most {MaxDisplayNameLength} characters and no control character." });
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    // One @ with something on both sides, and nothing a mail header could be broken with.
    public static bool IsEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength || email.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1 && email.AsSpan().IndexOfAny("<>,;") < 0;
    }

    public static bool IsDisplayName(string? name) =>
        name is null || (name.Length <= MaxDisplayNameLength && !name.Any(char.IsControl));
}

// Identity's Data Protection tokens with the lifetimes of cms.Members and a purpose of their own, so that a token of the
// admin pool is never one of a member. A token carries the member's security stamp, which verifying the address and
// resetting the password both change, so it is spent once it is used.
internal sealed class MemberEmailTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<MemberEmailTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<CmsMember>> logger)
    : DataProtectorTokenProvider<CmsMember>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "FluentCmsMemberEmail";
}

internal sealed class MemberEmailTokenProviderOptions : DataProtectionTokenProviderOptions;

internal sealed class MemberPasswordResetTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<MemberPasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<CmsMember>> logger)
    : DataProtectorTokenProvider<CmsMember>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "FluentCmsMemberPasswordReset";
}

internal sealed class MemberPasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions;
