using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Seeding;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Seeding;

// Runs the seeders that were added to the builder. What was applied is kept in the instance-wide settings area "seeding", and
// a seeder is claimed in the same document with a write that names the version it read, so two instances never run one at once.
internal sealed class SeedRunner(
    IServiceProvider services,
    IOperationContextAccessor accessor,
    LocaleRegistry locales,
    SiteManager sites,
    SeedingBuilder options,
    SettingsService settings,
    SettingsRegistry registry,
    SystemDbContext database,
    TimeProvider time,
    ILogger<SeedRunner> logger) : ISeedRunner
{
    private const int Attempts = 50;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.Types.Count == 0)
        {
            return;
        }

        var site = new SiteId(DefaultSite.Id);
        var locale = (await locales.GetAsync(site, cancellationToken)).Default;
        var descriptor = registry.For(typeof(SeedingState));
        var previous = accessor.Current;
        var localesEnsured = false;

        try
        {
            accessor.Current = new OperationContext(Principal.System, Origin.Seed, site, locale, ClientId: null, Guid.CreateVersion7().ToString(), time.GetUtcNow());

            foreach (var type in options.Types)
            {
                var seeder = (ISeeder)services.GetService(type)!;
                if (!await ClaimAsync(descriptor, seeder, cancellationToken))
                {
                    continue;
                }

                try
                {
                    if (!localesEnsured)
                    {
                        foreach (var required in options.Locales)
                        {
                            await sites.EnsureLocaleAsync(site.Value, required, cancellationToken);
                        }

                        localesEnsured = true;
                    }

                    await seeder.SeedAsync(new SeedContext(services), cancellationToken);
                }
                catch (Exception exception)
                {
                    SeedLog.Failed(logger, exception, seeder.Name, seeder.Version);
                    await ReleaseAsync(descriptor, seeder);
                    throw;
                }

                await UpdateAsync(descriptor, seeder, applied: true, cancellationToken);
                SeedLog.Applied(logger, seeder.Name, seeder.Version);
            }
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    // False when the seeder was applied at this version already, another instance is running it, or this instance lost the race for it.
    private async Task<bool> ClaimAsync(SettingsDescriptor descriptor, ISeeder seeder, CancellationToken cancellationToken)
    {
        var (state, version) = await ReadAsync(descriptor, cancellationToken);
        var record = state.Seeders.GetValueOrDefault(seeder.Name);

        if (record is not null && record.Applied >= seeder.Version)
        {
            SeedLog.Skipped(logger, seeder.Name, seeder.Version, "already applied");
            return false;
        }

        if (record is { ClaimedAt: { } at } && record.ClaimedVersion >= seeder.Version && time.GetUtcNow() - at < options.ClaimTimeout)
        {
            SeedLog.Skipped(logger, seeder.Name, seeder.Version, "another instance is running it");
            return false;
        }

        record ??= new SeedRecord();
        record.ClaimedVersion = seeder.Version;
        record.ClaimedAt = time.GetUtcNow();
        state.Seeders[seeder.Name] = record;

        try
        {
            await settings.WriteAsync(descriptor, null, descriptor.Write(state), version, cancellationToken);
            return true;
        }
        catch (ValidationException exception) when (IsConflict(exception))
        {
            SeedLog.Skipped(logger, seeder.Name, seeder.Version, "another instance claimed it first");
            return false;
        }
    }

    private async Task ReleaseAsync(SettingsDescriptor descriptor, ISeeder seeder)
    {
        try
        {
            await UpdateAsync(descriptor, seeder, applied: false, CancellationToken.None);
        }
        catch (Exception exception) when (exception is ValidationException or DbUpdateException or InvalidOperationException)
        {
            // The claim then runs out by itself.
        }
    }

    // Another instance may be writing the record of a different seeder at the same time, so the document is read again and the write retried.
    private async Task UpdateAsync(SettingsDescriptor descriptor, ISeeder seeder, bool applied, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var (state, version) = await ReadAsync(descriptor, cancellationToken);
            var record = state.Seeders.GetValueOrDefault(seeder.Name) ?? new SeedRecord();
            if (applied)
            {
                record.Applied = Math.Max(record.Applied, seeder.Version);
            }

            if (record.ClaimedVersion <= seeder.Version)
            {
                record.ClaimedVersion = 0;
                record.ClaimedAt = null;
            }

            state.Seeders[seeder.Name] = record;

            try
            {
                await settings.WriteAsync(descriptor, null, descriptor.Write(state), version, cancellationToken);
                return;
            }
            catch (ValidationException exception) when (IsConflict(exception))
            {
            }
        }

        throw new InvalidOperationException($"The record of seeder {seeder.Name} could not be saved, because the seeding settings kept changing.");
    }

    // Straight from the table, so that a write by another instance is seen at once and not after the settings cache has expired.
    private async Task<(SeedingState State, int Version)> ReadAsync(SettingsDescriptor descriptor, CancellationToken cancellationToken)
    {
        var row = await database.Set<SettingRow>().AsNoTracking()
            .Where(item => item.Key == descriptor.Area && item.SiteId == null)
            .Select(item => new { item.Json, item.Version })
            .FirstOrDefaultAsync(cancellationToken);

        return row is not null && descriptor.CanRead(row.Json)
            ? ((SeedingState)descriptor.Read(row.Json), row.Version)
            : (new SeedingState(), row?.Version ?? 0);
    }

    private static bool IsConflict(ValidationException exception) => exception.Errors.Any(error => error.Code == SettingsErrorCodes.VersionConflict);
}
