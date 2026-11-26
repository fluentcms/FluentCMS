using System.Globalization;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;
using static FluentCMS.Core.Commands.AdminCommandSupport;

namespace FluentCMS.Core.Commands;

// The keys of the authorization server (ADR 0008). These commands run in the host only: they read and write
// cms_signing_key, which no API reaches, and a running instance sees what they wrote within 30 seconds.
internal static class KeyCommandSupport
{
    public const string Use = "--use";

    public static CommandOption UseOption(string description) => new(Use, description, "use");

    // The uses the command is about: the one of --use, or both.
    public static IReadOnlyList<string> Uses(CommandContext context) =>
        context.Arguments.Value(Use) switch
        {
            null => SigningKey.Uses,
            SigningKey.Signing => [SigningKey.Signing],
            SigningKey.Encryption => [SigningKey.Encryption],
            _ => throw new CommandException(CommandErrorCodes.InvalidValue, $"{Use} is {SigningKey.Signing}, the key that signs tokens, or {SigningKey.Encryption}, the key that encrypts codes and refresh tokens", CommandExitCodes.BadUsage),
        };

    public static string Name(string use) => use == SigningKey.Signing ? "signing" : "encryption";

    public static string Verb(string use) => use == SigningKey.Signing ? "signs" : "encrypts";
}

internal sealed class KeysListCommand : HostCommand
{
    public override string Name => "keys list";

    public override string Description => "Lists the keys of the authorization server with their states and dates, and the configured signing certificates by thumbprint and validity; a key itself is never shown.";

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var security = context.Services.GetRequiredService<SecurityOptions>();
        var now = context.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Read as they are: listing makes no key.
        var keys = await context.Services.GetRequiredService<SigningKeyStore>().ReadAsync([], cancellationToken);
        await WriteTableAsync(
            context.Output,
            ["ID", "USE", "ALGORITHM", "STATE", "CREATED", "ACTIVE FROM", "RETIRES"],
            keys.Select(key => (IReadOnlyList<string>)
                [key.Id.ToString("D"), key.Use, key.Algorithm, State(key, keys, security, now), Moment(key.CreatedAt), Moment(key.ActiveFrom), Moment(key.RetiredAt)]));

        var certificates = context.Services.GetRequiredService<SigningCertificates>().Describe();
        if (certificates.Count > 0)
        {
            await context.Output.WriteLineAsync();
            await WriteTableAsync(
                context.Output,
                ["CERTIFICATE", "ROLE", "NOT BEFORE", "NOT AFTER"],
                certificates.Select(certificate => (IReadOnlyList<string>)
                    [certificate.Thumbprint, certificate.Signs ? "signs" : "validates", Moment(certificate.NotBefore), Moment(certificate.NotAfter)]));
        }

        return CommandExitCodes.Success;
    }

    // While a certificate signs, a generated signing key that is still accepted only validates what it signed before.
    private static string State(SigningKey key, IReadOnlyList<SigningKey> keys, SecurityOptions security, DateTimeOffset now)
    {
        var active = security.SigningCertificate.IsConfigured && key.Use == SigningKey.Signing
            ? null
            : SigningKeyStates.ActiveOf(keys.Where(other => other.Use == key.Use), now);
        return SigningKeyStates.StateOf(key, active, now).ToString().ToLowerInvariant();
    }
}

// Makes the successor now, with the lead and the overlap the job gives one. It does nothing for a use that has an upcoming
// key, so a second run, or a run beside the job's, makes no second key.
internal sealed class KeysRotateCommand : HostCommand
{
    public override string Name => "keys rotate";

    public override string Description => "Makes the next signing and encryption key of the authorization server: it is accepted at once, signs or encrypts after KeyRotation:PublishLead, and the key before it stays accepted for the overlap after that, so nobody is signed out. Does nothing for a use that has an upcoming key.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        KeyCommandSupport.UseOption($"Only the key of this use: {SigningKey.Signing} or {SigningKey.Encryption}. Both when left out."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var uses = KeyCommandSupport.Uses(context);
        var security = context.Services.GetRequiredService<SecurityOptions>();
        if (security.SigningCertificate.IsConfigured)
        {
            if (context.Arguments.Value(KeyCommandSupport.Use) == SigningKey.Signing)
            {
                throw new CommandException(
                    CommandErrorCodes.CertificateSigns,
                    $"{SigningCertificates.Key} signs the tokens, so there is no generated signing key to rotate; to replace the certificate, set the new one there and list the one it replaces under {SigningCertificates.PreviousKey}",
                    CommandExitCodes.BadUsage);
            }

            await context.Error.WriteLineAsync($"{SigningCertificates.Key} signs the tokens, so only the encryption key is rotated.");
            uses = [SigningKey.Encryption];
        }

        var rotation = security.KeyRotation;
        var overlap = KeyOverlap.Of(
            context.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue,
            context.Services.GetRequiredService<PreviewTokenOptions>(),
            rotation);
        var store = context.Services.GetRequiredService<SigningKeyStore>();
        foreach (var use in uses)
        {
            var (key, made) = await store.RotateAsync(use, rotation.PublishLead, overlap, cancellationToken);
            await context.Output.WriteLineAsync(made
                ? $"Made the {KeyCommandSupport.Name(use)} key {key.Id:D}. It is accepted now and {KeyCommandSupport.Verb(use)} from {Moment(key.ActiveFrom)} UTC; the key before it is accepted until {Moment(key.ActiveFrom + overlap)} UTC and removed by the job {OidcKeysJob.RecurringName} after that."
                : $"The {KeyCommandSupport.Name(use)} key {key.Id:D} is upcoming already and {KeyCommandSupport.Verb(use)} from {Moment(key.ActiveFrom)} UTC, so no key was made.");
        }

        return CommandExitCodes.Success;
    }
}

// For a key that may have leaked, where a rotation would go on accepting it for the overlap. It retires every stored key
// at once and makes new ones with no lead, so every access token, refresh token, code, logout hint and preview token is
// refused from then on: at once on an instance that restarts, within 30 seconds on one that does not. It also revokes
// every valid token and every session, the ad-hoc authorizations, of every user, member and client, so that an instance
// that has not read the keys yet refuses the tokens the server did issue, and the lists of sessions stay true; that does
// not stop a forged token, which names no entry, and is why the keys are retired. Consents stay, and so does the sign-in
// cookie of the hosted pages, which is under Data Protection, so a browser gets new tokens without a prompt. The keys are
// not read, so the command works on rows the key ring can no longer read. One activity row for the instance records it.
internal sealed class KeysRevokeCommand : HostCommand
{
    public const string Force = "--force";

    public const string Action = "token_keys.revoked";

    public override string Name => "keys revoke";

    public override string Description => $"For a key that may have leaked: retires every key of the authorization server at once, makes new ones that sign and encrypt at once, and revokes every token and session of every user, member and client, who sign in again; consents are kept. Changes nothing without {Force}. To replace a key without signing anyone out, use 'keys rotate'.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        KeyCommandSupport.UseOption($"Only the keys of this use: {SigningKey.Signing} or {SigningKey.Encryption}. Both when left out. The tokens and sessions are revoked either way."),
        new(Force, "Does it. Without it the command changes nothing and says what it would end."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var uses = KeyCommandSupport.Uses(context);
        var certificate = context.Services.GetRequiredService<SecurityOptions>().SigningCertificate.IsConfigured;
        IReadOnlyCollection<string> generated = certificate ? [SigningKey.Encryption] : SigningKey.Uses;
        var store = context.Services.GetRequiredService<SigningKeyStore>();
        var database = context.Services.GetRequiredService<SystemDbContext>();

        if (!context.Arguments.Has(Force))
        {
            var keys = await store.CountAcceptedAsync(uses, cancellationToken);
            var valid = await database.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().CountAsync(token => token.Status == OpenIddictConstants.Statuses.Valid, cancellationToken);
            var sessions = await database.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
                .CountAsync(authorization => authorization.Status == OpenIddictConstants.Statuses.Valid && authorization.Type == OpenIddictConstants.AuthorizationTypes.AdHoc, cancellationToken);
            throw new CommandException(
                CommandErrorCodes.RevocationNotForced,
                $"nothing was changed. With {Force} this retires {keys} keys (use {string.Join(" and ", uses)}) at once, makes {uses.Count(generated.Contains)} that are active at once, and revokes {valid} valid tokens and {sessions} sessions of every user, member and client, who all sign in again or get new tokens from the sign-in cookie of the hosted pages; consents are kept. Pass {Force} when a key may have leaked; to replace a key without signing anyone out, run 'fluentcms keys rotate'");
        }

        // The keys first: from here on nothing signed with them is accepted by an instance that reads or restarts.
        var revocation = await store.RevokeAsync(uses, generated, cancellationToken);
        var tokens = await context.Services.GetRequiredService<IOpenIddictTokenManager>()
            .RevokeAsync(subject: null, client: null, OpenIddictConstants.Statuses.Valid, type: null, cancellationToken);
        var authorizations = await context.Services.GetRequiredService<IOpenIddictAuthorizationManager>()
            .RevokeAsync(subject: null, client: null, OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.AdHoc, cancellationToken);

        OidcLog.KeysRevoked(context.Services.GetRequiredService<ILogger<KeysRevokeCommand>>(), revocation.Retired.Count, string.Join(',', uses), revocation.At, tokens, authorizations);

        // One row for the instance, and none per user: user.sessions_ended is what a user or an administrator ended.
        var operation = context.Services.GetRequiredService<IOperationContextAccessor>().Current;
        var source = (operation is null ? EventSource.FromSystem(revocation.At) : EventSource.From(operation)) with { SiteId = Guid.Empty };
        await context.Services.GetRequiredService<IActivityLog>().WriteAsync(
            new ActivityEntry(Action)
            {
                TargetType = "token_keys",
                TargetLabel = string.Join(',', uses),
                Details = new Dictionary<string, string?>
                {
                    ["uses"] = string.Join(',', uses),
                    ["retired"] = string.Join(',', revocation.Retired.Select(key => key.Id.ToString("D"))),
                    ["made"] = string.Join(',', revocation.Made.Select(key => key.Id.ToString("D"))),
                    ["tokens"] = tokens.ToString(CultureInfo.InvariantCulture),
                    ["sessions"] = authorizations.ToString(CultureInfo.InvariantCulture),
                },
            },
            source,
            cancellationToken);

        await context.Output.WriteLineAsync($"Retired {revocation.Retired.Count} keys (use {string.Join(" and ", uses)}) at {Moment(revocation.At)} UTC and revoked {tokens} tokens and {authorizations} sessions; consents are kept.");
        foreach (var key in revocation.Made)
        {
            await context.Output.WriteLineAsync($"Made the {KeyCommandSupport.Name(key.Use)} key {key.Id:D}, which {KeyCommandSupport.Verb(key.Use)} now.");
        }

        await context.Output.WriteLineAsync("Restart every instance now. One that is not restarted reads the keys within 30 seconds, and until then accepts the retired keys and refuses the tokens of the new ones.");
        if (certificate && uses.Contains(SigningKey.Signing))
        {
            await context.Error.WriteLineAsync($"{SigningCertificates.Key} signs the tokens and this command does not change it. If it is the certificate that may have leaked, replace it without listing it under {SigningCertificates.PreviousKey} and restart every instance.");
        }

        return CommandExitCodes.Success;
    }
}
