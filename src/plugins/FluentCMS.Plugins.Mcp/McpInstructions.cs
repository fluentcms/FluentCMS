using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Mcp;

// What a site tells its agents, read once per request: the guidelines, and the instructions by content type. The
// instructions of a type that is no longer in the schema are left out. Nothing is checked here: the guidelines are for
// every agent of the site, and whoever serves the instructions of a type serves them to a caller that reaches the type.
internal sealed class McpInstructions(PluginDbContext<McpPlugin> database, SchemaModel schema)
{
    private readonly Dictionary<Guid, (string? Guidelines, IReadOnlyDictionary<string, string> ByType)> _sites = [];

    public async ValueTask<string?> GuidelinesAsync(SiteId site, CancellationToken cancellationToken) =>
        (await OfSiteAsync(site, cancellationToken)).Guidelines;

    public async ValueTask<IReadOnlyDictionary<string, string>> ByTypeAsync(SiteId site, CancellationToken cancellationToken) =>
        (await OfSiteAsync(site, cancellationToken)).ByType;

    private async ValueTask<(string? Guidelines, IReadOnlyDictionary<string, string> ByType)> OfSiteAsync(SiteId site, CancellationToken cancellationToken)
    {
        if (!_sites.TryGetValue(site.Value, out var instructions))
        {
            var rows = await database.Set<McpInstruction>().AsNoTracking()
                .Where(instruction => instruction.SiteId == site.Value)
                .Select(instruction => new { instruction.Type, instruction.Text })
                .ToListAsync(cancellationToken);
            instructions = (
                rows.FirstOrDefault(row => row.Type is null)?.Text,
                rows.Where(row => row.Type is not null && schema.FindContentType(row.Type) is not null).ToDictionary(row => row.Type!, row => row.Text, StringComparer.Ordinal));
            _sites[site.Value] = instructions;
        }

        return instructions;
    }
}
