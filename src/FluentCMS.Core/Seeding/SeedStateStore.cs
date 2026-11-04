using FluentCMS.Content;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Seeding;

// Reads and writes the "seeding" document. A write names the version it read, so that two writers never overwrite each other.
internal sealed class SeedStateStore(SettingsService settings, SettingsRegistry registry, SystemDbContext database)
{
    private const int Attempts = 50;

    private SettingsDescriptor Descriptor => registry.For(typeof(SeedingState));

    // Straight from the table, so that a write by another instance is seen at once and not after the settings cache has expired.
    public async Task<(SeedingState State, int Version)> ReadAsync(CancellationToken cancellationToken)
    {
        var row = await database.Set<SettingRow>().AsNoTracking()
            .Where(item => item.Key == Descriptor.Area && item.SiteId == null)
            .Select(item => new { item.Json, item.Version })
            .FirstOrDefaultAsync(cancellationToken);

        return row is not null && Descriptor.CanRead(row.Json)
            ? ((SeedingState)Descriptor.Read(row.Json), row.Version)
            : (new SeedingState(), row?.Version ?? 0);
    }

    // False when the document changed since it was read.
    public async Task<bool> TryWriteAsync(SeedingState state, int version, CancellationToken cancellationToken)
    {
        try
        {
            await settings.WriteAsync(Descriptor, null, Descriptor.Write(state), version, cancellationToken);
            return true;
        }
        catch (ValidationException exception) when (exception.Errors.Any(error => error.Code == SettingsErrorCodes.VersionConflict))
        {
            return false;
        }
    }

    // Applies a change to the current document and writes it, reading again when another writer got there first. The change returns
    // false to leave the document alone, as when the claim it depends on is no longer the caller's.
    public async Task<bool> MutateAsync(Func<SeedingState, bool> change, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var (state, version) = await ReadAsync(cancellationToken);
            if (!change(state))
            {
                return false;
            }

            if (await TryWriteAsync(state, version, cancellationToken))
            {
                return true;
            }
        }

        throw new InvalidOperationException("The seeding settings could not be saved, because they kept changing.");
    }
}
