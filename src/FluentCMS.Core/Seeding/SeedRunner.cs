using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Seeding;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Seeding;

// Runs the seeders that were added to the builder. What was applied is kept in the instance-wide settings area "seeding". A run holds
// one claim in the same document for all its seeders, taken with a write that names the version it read and renewed while the run
// lasts, so two instances never seed at once and a claim older than the timeout means its owner is gone.
internal sealed class SeedRunner(
    IServiceProvider services,
    IOperationContextAccessor accessor,
    LocaleRegistry locales,
    SiteManager sites,
    SeedingBuilder options,
    SeedStateStore store,
    TimeProvider time,
    ILogger<SeedRunner> logger) : ISeedRunner
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.Types.Count == 0)
        {
            return;
        }

        var seeders = options.Types.Select(type => (ISeeder)services.GetService(type)!).ToList();
        var (state, version) = await store.ReadAsync(cancellationToken);
        var pending = seeders.Where(seeder => state.Seeders.GetValueOrDefault(seeder.Name)?.Applied is not { } applied || applied < seeder.Version).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var site = new SiteId(DefaultSite.Id);
        var locale = (await locales.GetAsync(site, cancellationToken)).Default;
        var previous = accessor.Current;
        accessor.Current = new OperationContext(Principal.System, Origin.Seed, site, locale, ClientId: null, Guid.CreateVersion7().ToString(), time.GetUtcNow());

        try
        {
            await RunClaimedAsync(pending, state, version, site, cancellationToken);
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    private async Task RunClaimedAsync(List<ISeeder> pending, SeedingState state, int version, SiteId site, CancellationToken cancellationToken)
    {
        if (state.Claim is { } held && time.GetUtcNow() - held.At < options.ClaimTimeout)
        {
            SeedLog.Skipped(logger, "another instance is seeding");
            return;
        }

        var claimId = Guid.CreateVersion7();
        state.Claim = new SeedClaim { Id = claimId, At = time.GetUtcNow() };
        if (!await store.TryWriteAsync(state, version, cancellationToken))
        {
            SeedLog.Skipped(logger, "another instance claimed the seed first");
            return;
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var stopHeartbeat = new CancellationTokenSource();
        var lost = false;
        var heartbeat = Task.Run(() => HeartbeatAsync(claimId, () => { lost = true; run.Cancel(); }, stopHeartbeat.Token), CancellationToken.None);

        try
        {
            foreach (var seeder in pending)
            {
                foreach (var required in options.Locales)
                {
                    await sites.EnsureLocaleAsync(site.Value, required, run.Token);
                }

                var applied = state.Seeders.GetValueOrDefault(seeder.Name)?.Applied ?? 0;
                try
                {
                    await seeder.SeedAsync(new SeedContext(services, applied), run.Token);
                }
                catch (OperationCanceledException) when (lost)
                {
                    SeedLog.ClaimLost(logger);
                    return;
                }
                catch (Exception exception)
                {
                    SeedLog.Failed(logger, exception, seeder.Name, seeder.Version);
                    await ReleaseAsync(claimId);
                    throw;
                }

                var marked = await store.MutateAsync(
                    current =>
                    {
                        if (current.Claim?.Id != claimId)
                        {
                            return false;
                        }

                        var record = current.Seeders.GetValueOrDefault(seeder.Name) ?? new SeedRecord();
                        record.Applied = Math.Max(record.Applied, seeder.Version);
                        current.Seeders[seeder.Name] = record;
                        return true;
                    },
                    CancellationToken.None);
                if (!marked)
                {
                    SeedLog.ClaimLost(logger);
                    return;
                }

                SeedLog.Applied(logger, seeder.Name, seeder.Version);
            }

            await ReleaseAsync(claimId);
        }
        catch (OperationCanceledException) when (lost)
        {
            SeedLog.ClaimLost(logger);
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(claimId);
            throw;
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
    }

    // Clears the claim if it is still this run's; a claim that another run took over is left alone.
    private async Task ReleaseAsync(Guid claimId)
    {
        try
        {
            await store.MutateAsync(
                current =>
                {
                    if (current.Claim?.Id != claimId)
                    {
                        return false;
                    }

                    current.Claim = null;
                    return true;
                },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The claim then runs out by itself.
            SeedLog.HeartbeatFailed(logger, exception);
        }
    }

    // On a store of its own, because the scope of the run is in use by the seeder.
    private async Task HeartbeatAsync(Guid claimId, Action onLost, CancellationToken stop)
    {
        var interval = options.ClaimTimeout / 3;
        try
        {
            while (true)
            {
                await Task.Delay(interval, time, stop);

                try
                {
                    await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                    var renewed = await scope.ServiceProvider.GetRequiredService<SeedStateStore>().MutateAsync(
                        current =>
                        {
                            if (current.Claim?.Id != claimId)
                            {
                                return false;
                            }

                            current.Claim.At = time.GetUtcNow();
                            return true;
                        },
                        stop);
                    if (!renewed)
                    {
                        onLost();
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    SeedLog.HeartbeatFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
