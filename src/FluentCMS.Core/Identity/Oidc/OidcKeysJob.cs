using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace FluentCMS.Core.Identity.Oidc;

internal sealed record OidcKeysPayload;

// How long a key stays accepted after its successor signs or encrypts: the longest lifetime of anything it signed or
// encrypted, the 30 seconds in which a used refresh token is taken again, and 5 minutes for clocks. A token is verified by
// its signature before its entry is read, so the entries in the database change nothing here (ADR 0008).
internal static class KeyOverlap
{
    public static readonly TimeSpan ReuseLeeway = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(5);

    public static TimeSpan After(TimeSpan longestLifetime) => longestLifetime + ReuseLeeway + ClockAllowance;

    // What the options of FluentCMS give when the host starts, before the options of OpenIddict can be built.
    public static TimeSpan AtStart(ClientOptions clients, PreviewTokenOptions preview) =>
        After(Longest(clients.AccessTokenLifetime, clients.RefreshTokenLifetime, preview.MaximumLifetime));

    // The configured overlap when it is longer than the derived one, which also counts the lifetimes a host set on
    // OpenIddict itself.
    public static TimeSpan Of(OpenIddictServerOptions server, PreviewTokenOptions preview, KeyRotationOptions rotation)
    {
        var derived = After(Longest(
            server.AccessTokenLifetime,
            server.AuthorizationCodeLifetime,
            server.DeviceCodeLifetime,
            server.IdentityTokenLifetime,
            server.IssuedTokenLifetime,
            server.RefreshTokenLifetime,
            server.RequestTokenLifetime,
            server.UserCodeLifetime,
            preview.MaximumLifetime));
        return rotation.Overlap is { } configured && configured > derived ? configured : derived;
    }

    private static TimeSpan Longest(params TimeSpan?[] lifetimes) => lifetimes.Max() ?? TimeSpan.Zero;
}

// Looks after the keys of cms_signing_key once a day (ADR 0008). It removes the rows that have retired, gives the generated
// signing keys that a certificate replaced a date to retire at, and moves a retirement later when a token lifetime was
// raised since it was set. When routine rotation is on, it makes the successor of an active key that is Interval minus
// PublishLead old, so that the successor signs when the key is Interval old; an active key that is to retire gets its
// successor whether rotation is on or not, which is the case of generated keys that sign again after a certificate did. A run
// writes a few rows, so it is not resumable, and a run that is missed only makes a rotation late.
internal sealed class OidcKeysJob(
    SigningKeyStore store,
    SecurityOptions security,
    PreviewTokenOptions preview,
    IOptionsMonitor<OpenIddictServerOptions> server,
    TimeProvider time)
    : IJob<OidcKeysPayload>
{
    public const string Type = "oidc.keys";

    public const string RecurringName = "oidc.keys";

    public const string Schedule = "45 4 * * *";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);

    public async Task ExecuteAsync(OidcKeysPayload payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(jobContext);

        var rotation = security.KeyRotation;
        var certificate = security.SigningCertificate.IsConfigured;
        var overlap = KeyOverlap.Of(server.CurrentValue, preview, rotation);
        await store.UpkeepAsync(certificate, overlap, jobContext.CancellationToken);

        var now = time.GetUtcNow();
        var stored = await store.ReadAsync([], jobContext.CancellationToken);
        foreach (var use in SigningKey.Uses.Where(use => !(certificate && use == SigningKey.Signing)))
        {
            var accepted = stored.Where(key => key.Use == use && !SigningKeyStates.IsRetired(key, now)).ToList();
            if (SigningKeyStates.ActiveOf(accepted, now) is not { } active || accepted.Exists(key => key.ActiveFrom > now))
            {
                continue;
            }

            if (active.RetiredAt is not null || (rotation.Enabled && now >= active.ActiveFrom + rotation.Interval - rotation.PublishLead))
            {
                await store.RotateAsync(use, rotation.PublishLead, overlap, jobContext.CancellationToken);
            }
        }
    }
}

// Stores the schedule a minute after the host starts and then keeps trying while the database cannot be reached, as the
// prune job's does.
internal sealed class OidcKeysScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<OidcKeysScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstAttempt = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wait = FirstAttempt;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await TryScheduleAsync(stoppingToken))
            {
                return;
            }

            wait = Retry;
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<OidcKeysJob, OidcKeysPayload>(OidcKeysJob.RecurringName, OidcKeysJob.Schedule, new OidcKeysPayload(), queue: null, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            OidcLog.KeysScheduleFailed(logger, exception);
            return false;
        }
    }
}

// A configured overlap shorter than the longest lifetime of a token would retire a key while what it signed is still good,
// so the host does not start with one. It is checked with the lifetimes of ClientOptions, and not with SecurityOptions: a
// package may need SecurityOptions to configure its client, as the admin does, so SecurityOptions must not need ClientOptions.
internal sealed class KeyOverlapCheck(SecurityOptions security, PreviewTokenOptions preview) : IValidateOptions<ClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        if (name != Options.DefaultName || security.KeyRotation.Overlap is not { } overlap)
        {
            return ValidateOptionsResult.Success;
        }

        var least = KeyOverlap.AtStart(options, preview);
        if (overlap < least)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{FluentCmsOptions.Section}:{SecurityOptions.Section}:{KeyRotationOptions.Section}:{nameof(KeyRotationOptions.Overlap)} is {overlap}; it must be at least {least}, which is the longest lifetime of a token ({FluentCmsOptions.Section}:{ClientOptions.Section}:{nameof(ClientOptions.RefreshTokenLifetime)}, {nameof(ClientOptions.AccessTokenLifetime)} and {FluentCmsOptions.Section}:{PreviewTokenOptions.Section}:{nameof(PreviewTokenOptions.MaximumLifetime)}), {KeyOverlap.ReuseLeeway.TotalSeconds:0} seconds for a refresh token used twice and {KeyOverlap.ClockAllowance.TotalMinutes:0} minutes for clocks. Leave it unset to have exactly that.");
        }

        return ValidateOptionsResult.Success;
    }
}
