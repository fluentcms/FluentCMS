using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// Stored documents whose secrets were written before secrets were protected at rest, by area, for the startup report. Only
// the areas and a count are named, never a site's value. The check never fails the report: what it cannot read is logged.
internal sealed class PlaintextSecretsReport(IServiceScopeFactory scopes, SettingsRegistry registry, ILogger<PlaintextSecretsReport> logger) : IInsecureDefaultSource
{
    public const string Id = "settings-secrets-unprotected";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        var areas = registry.Descriptors.Where(descriptor => descriptor.SecretKeys.Count > 0).ToDictionary(descriptor => descriptor.Area, StringComparer.Ordinal);
        if (areas.Count == 0)
        {
            return [];
        }

        // The report asks its sources synchronously.
        return FindAsync(areas).GetAwaiter().GetResult() is { } found ? [found] : [];
    }

    private async Task<InsecureDefault?> FindAsync(Dictionary<string, SettingsDescriptor> areas)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var keys = areas.Keys.ToList();
            var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<SettingRow>().AsNoTracking()
                .Where(row => keys.Contains(row.Key))
                .Select(row => new { row.Key, row.Json })
                .ToListAsync();

            var plain = rows.Where(row => SettingsProtection.HoldsPlaintext(areas[row.Key], row.Json)).ToList();
            if (plain.Count == 0)
            {
                return null;
            }

            var names = string.Join(", ", plain.Select(row => row.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return new InsecureDefault
            {
                Id = Id,
                Message = $"{plain.Count} stored settings documents hold secrets that were written before secrets were protected at rest, so a reader of the database or of a backup can read them: areas {names}. Each is protected when its document is next written; save those areas once for each site and for the instance defaults, and replace secrets that a backup may have exposed.",
            };
        }
        catch (Exception exception)
        {
            SettingsLog.PlaintextNotChecked(logger, exception.GetType().Name);
            return null;
        }
    }
}
