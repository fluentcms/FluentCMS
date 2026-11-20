using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// Stored documents with secrets that are not protected, written before secrets were, or that the key ring cannot unprotect,
// by area, for the startup report. Only the areas and a count are named, never a value. The check never fails the report:
// what it cannot read is logged.
internal sealed class SecretSettingsReport(IServiceScopeFactory scopes, SettingsRegistry registry, SettingsProtection protection, ILogger<SecretSettingsReport> logger) : IInsecureDefaultSource
{
    public const string Unprotected = "settings-secrets-unprotected";
    public const string Unreadable = "settings-secrets-unreadable";

    private const string Ring = "The protection is only as strong as the Data Protection key ring, which is kept in the same database and is unencrypted without a key ring certificate; losing the ring or replacing its certificate without keeping the old one loses every secret setting.";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        var areas = registry.Descriptors.Where(descriptor => descriptor.SecretKeys.Count > 0).ToDictionary(descriptor => descriptor.Area, StringComparer.Ordinal);
        if (areas.Count == 0)
        {
            return [];
        }

        // The report asks its sources synchronously.
        return FindAsync(areas).GetAwaiter().GetResult();
    }

    private async Task<List<InsecureDefault>> FindAsync(Dictionary<string, SettingsDescriptor> areas)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var keys = areas.Keys.ToList();
            var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SettingRow>().AsNoTracking()
                .Where(row => keys.Contains(row.Key))
                .Select(row => new { row.Key, row.SiteId, row.Json })
                .ToListAsync();

            var found = rows.Select(row => (row.Key, State: protection.Inspect(areas[row.Key], row.Json, row.SiteId))).ToList();
            List<InsecureDefault> defaults = [];
            if (Of(found, StoredSecrets.Plaintext) is var (plainCount, plainAreas) && plainCount > 0)
            {
                defaults.Add(new InsecureDefault
                {
                    Id = Unprotected,
                    Message = $"{plainCount} stored settings documents hold secrets that were written before secrets were protected at rest, so a reader of the database or of a backup can read them: areas {plainAreas}. Each is protected when its document is next written; save those areas once for each site and for the instance defaults, and replace secrets that a backup may have exposed. {Ring}",
                });
            }

            if (Of(found, StoredSecrets.Unreadable) is var (lostCount, lostAreas) && lostCount > 0)
            {
                defaults.Add(new InsecureDefault
                {
                    Id = Unreadable,
                    Message = $"{lostCount} stored settings documents hold secrets that the Data Protection key ring cannot unprotect, so they read as their code defaults: areas {lostAreas}. The rows keep them until a write sets them; restore the key ring, or its certificate, that protected them, or set those secrets again. {Ring}",
                });
            }

            return defaults;
        }
        catch (Exception exception)
        {
            SettingsLog.SecretsNotChecked(logger, exception.GetType().Name);
            return [];
        }
    }

    private static (int Count, string Areas) Of(List<(string Key, StoredSecrets State)> found, StoredSecrets state)
    {
        var matching = found.Where(row => row.State == state).ToList();
        return (matching.Count, string.Join(", ", matching.Select(row => row.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
    }
}
