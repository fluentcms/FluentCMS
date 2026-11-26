using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
