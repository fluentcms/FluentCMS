using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentCMS.Admin.Account;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The calls of the account page (A-09) into the admin's own records, as the built-in Editor.
public sealed class AccountTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheEditorEnrolsTwoFactorAndTurnsItOffAgain()
    {
        await using var admin = new AdminHost();
        var editor = await admin.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var client = await admin.SignInAsync(editor);
        var account = client.Client;

        (await account.GetTwoFactorAsync(Token)).Enabled.ShouldBeFalse();
        var setup = await account.EnrolAuthenticatorAsync(Token);
        setup.AuthenticatorUri.ShouldStartWith("otpauth://totp/");
        setup.AuthenticatorUri.ShouldContain($"secret={setup.SharedKey}");
        QrCode.Encode(setup.AuthenticatorUri).Version.ShouldBeLessThanOrEqualTo(QrCode.MaxVersion);
        (await account.GetTwoFactorAsync(Token)).Pending.ShouldBeTrue();

        var wrong = await Should.ThrowAsync<ApiProblemException>(() => account.VerifyAuthenticatorAsync(WrongCode(admin, setup.SharedKey), Token));
        AccountProblems.Has(wrong, AccountProblems.InvalidTwoFactorCode).ShouldBeTrue();

        var codes = await account.VerifyAuthenticatorAsync(NextCode(admin, setup.SharedKey), Token);
        codes.Count.ShouldBe(10);

        // The new security stamp revoked the user's sessions, this one among them (#3202).
        AccountProblems.IsSignedOut(await Should.ThrowAsync<ApiProblemException>(() => account.GetTwoFactorAsync(Token))).ShouldBeTrue();
        using var signedInAgain = await admin.SignInAsync(editor);
        account = signedInAgain.Client;
        var on = await account.GetTwoFactorAsync(Token);
        on.Enabled.ShouldBeTrue();
        on.RecoveryCodesLeft.ShouldBe(10);

        var again = await Should.ThrowAsync<ApiProblemException>(() => account.EnrolAuthenticatorAsync(Token));
        AccountProblems.Has(again, AccountProblems.TwoFactorEnabled).ShouldBeTrue();

        var wrongForNewCodes = await Should.ThrowAsync<ApiProblemException>(() => account.NewRecoveryCodesAsync(WrongCode(admin, setup.SharedKey), Token));
        AccountProblems.Has(wrongForNewCodes, AccountProblems.InvalidTwoFactorCode).ShouldBeTrue();
        var fresh = await account.NewRecoveryCodesAsync(NextCode(admin, setup.SharedKey), Token);
        fresh.Count.ShouldBe(10);
        fresh.ShouldNotContain(codes[0]);

        // An old recovery code no longer turns it off; a new one does.
        var old = await Should.ThrowAsync<ApiProblemException>(() => account.DisableTwoFactorAsync(codes[0], Token));
        AccountProblems.Has(old, AccountProblems.InvalidTwoFactorCode).ShouldBeTrue();
        await account.DisableTwoFactorAsync(fresh[0], Token);
        AccountProblems.IsSignedOut(await Should.ThrowAsync<ApiProblemException>(() => account.GetTwoFactorAsync(Token))).ShouldBeTrue();
        using var signedInOnceMore = await admin.SignInAsync(editor);
        (await signedInOnceMore.Client.GetTwoFactorAsync(Token)).Enabled.ShouldBeFalse();
    }

    // The server sets two-factor up only for a token whose sign-in is a few minutes old (#3372): the page's call is refused
    // with the code it tells apart, and after a fresh sign-in it enrols and turns two-factor on as before.
    [Fact]
    public async Task ASignInTooOldEnrolsNothingAndAFreshOneEnrols()
    {
        await using var admin = new AdminHost();
        var editor = await admin.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var old = await admin.SignInAsync(editor);
        admin.Host.Time.Advance(TimeSpan.FromMinutes(11));

        var refused = await Should.ThrowAsync<ApiProblemException>(() => old.Client.EnrolAuthenticatorAsync(Token));
        AccountProblems.Has(refused, AccountProblems.RecentSignInRequired).ShouldBeTrue();
        AccountProblems.IsSignedOut(refused).ShouldBeFalse();
        (await old.Client.GetTwoFactorAsync(Token)).Pending.ShouldBeFalse();

        using var fresh = await admin.SignInAsync(editor);
        var setup = await fresh.Client.EnrolAuthenticatorAsync(Token);
        (await fresh.Client.VerifyAuthenticatorAsync(NextCode(admin, setup.SharedKey), Token)).Count.ShouldBe(10);
    }

    [Fact]
    public async Task TheEditorReadsItsLoginsAndPreferencesAndEndsItsSession()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.EditorName);
        var account = client.Client;

        (await account.GetMyLoginsAsync(Token)).ShouldBeEmpty();
        var missing = await Should.ThrowAsync<ApiProblemException>(() => account.RemoveMyLoginAsync("nowhere", Token));
        AccountProblems.Has(missing, AccountProblems.LoginNotFound).ShouldBeTrue();

        var preferences = await account.SetMyPreferencesAsync(new MyPreferences { Culture = "en", TimeZone = "Europe/Berlin" }, Token);
        preferences.Culture.ShouldBe("en");
        (await account.GetMyPreferencesAsync(Token)).TimeZone.ShouldBe("Europe/Berlin");

        var session = (await account.GetMySessionsAsync(Token)).ShouldHaveSingleItem();
        session.ClientId.ShouldNotBeNullOrEmpty();
        session.CreatedAt.ShouldNotBeNull();

        await account.EndMySessionAsync(session.Id, Token);

        // It was this client's own: its token is refused from the next request on.
        var ended = await Should.ThrowAsync<ApiProblemException>(() => account.GetMySessionsAsync(Token));
        AccountProblems.IsSignedOut(ended).ShouldBeTrue();
    }

    // RFC 6238 with the defaults of an authenticator app: SHA-1, six digits, 30 seconds.
    private static string Totp(string key, DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // TOTP is defined on HMAC-SHA1.
        var hash = HMACSHA1.HashData(Base32(key), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The server checks codes on the host's clock and takes a code of each 30-second step once, so each code the test gives
    // is of the next step.
    private static string NextCode(AdminHost admin, string key)
    {
        admin.Host.Time.Advance(TimeSpan.FromSeconds(30));
        return Totp(key, admin.Host.Time.GetUtcNow());
    }

    // A code no window of the key around the host's now gives.
    private static string WrongCode(AdminHost admin, string key)
    {
        var now = admin.Host.Time.GetUtcNow();
        var valid = Enumerable.Range(-2, 5).Select(step => Totp(key, now.AddSeconds(step * 30))).ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(0, 1_000_000).Select(code => code.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).First(code => !valid.Contains(code));
    }

    private static byte[] Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        return [.. bytes];
    }
}
