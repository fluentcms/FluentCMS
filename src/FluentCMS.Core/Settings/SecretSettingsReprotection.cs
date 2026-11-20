using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Settings;

// A stored document left as it is, by its area and scope; Keys names the secret keys the key ring cannot unprotect, and is
// empty for an area that no class of the host registers, whose secret keys are not known.
internal sealed record SecretSettingsLeft(string Area, Guid? Site, IReadOnlyList<string> Keys);

internal sealed record SecretSettingsReprotected(int Documents, IReadOnlyList<SecretSettingsLeft> Left);

// Protects every stored secret again under the current default key of the key ring, so that an older key or certificate can
// be retired without losing a secret setting. Each row is written back only over the version it was read at, which goes up
// as with any write, and is read again when another writer got there first; a row whose secrets are all under the current
// key already is not written. The values do not change, so nothing is raised.
internal sealed class SecretSettingsReprotection(SystemDbContext context, SettingsRegistry registry, SettingsProtection protection)
{
    private const int Attempts = 100;

    public async Task<SecretSettingsReprotected> RunAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Set<SettingRow>().AsNoTracking()
            .OrderBy(row => row.Key).ThenBy(row => row.SiteId)
            .Select(row => new StoredRow(row.Id, row.Key, row.SiteId, row.Json, row.Version))
            .ToListAsync(cancellationToken);

        var documents = 0;
        List<SecretSettingsLeft> left = [];
        foreach (var row in rows)
        {
            if (registry.Find(row.Key) is not { } descriptor)
            {
                if (row.Json.Contains(SettingsProtection.Prefix, StringComparison.Ordinal))
                {
                    left.Add(new SecretSettingsLeft(row.Key, row.SiteId, []));
                }
            }
            else if (await ReprotectAsync(descriptor, row, cancellationToken) is not { } unreadable)
            {
                documents++;
            }
            else if (unreadable.Count > 0)
            {
                left.Add(new SecretSettingsLeft(row.Key, row.SiteId, unreadable));
            }
        }

        return new SecretSettingsReprotected(documents, left);
    }

    // Null when the row was written again; otherwise the keys the key ring cannot unprotect, none when there was nothing to do.
    private async Task<IReadOnlyList<string>?> ReprotectAsync(SettingsDescriptor descriptor, StoredRow row, CancellationToken cancellationToken)
    {
        var current = row;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (protection.Reprotect(descriptor, current.Json, current.SiteId, out var unreadable) is not { } json)
            {
                return unreadable;
            }

            var version = current.Version;
            var updated = await context.Set<SettingRow>()
                .Where(item => item.Id == row.Id && item.Version == version)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Json, json).SetProperty(item => item.Version, version + 1), cancellationToken);
            if (updated > 0)
            {
                return null;
            }

            // Another writer got there first; what it wrote is checked again.
            if (await context.Set<SettingRow>().AsNoTracking()
                .Where(item => item.Id == row.Id)
                .Select(item => new StoredRow(item.Id, item.Key, item.SiteId, item.Json, item.Version))
                .FirstOrDefaultAsync(cancellationToken) is not { } again)
            {
                return [];
            }

            current = again;
        }

        throw new InvalidOperationException($"The settings area '{descriptor.Area}' kept changing while its secrets were protected again; run the pass again.");
    }

    private sealed record StoredRow(Guid Id, string Key, Guid? SiteId, string Json, int Version);
}
