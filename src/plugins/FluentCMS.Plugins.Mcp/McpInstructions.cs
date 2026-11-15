using FluentCMS.Authorization;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Mcp;

// What a site tells its agents, read once per request: the guidelines, and the instructions by content type. The
// instructions of a type that is no longer in the schema are left out, and a caller that is no agent of the site reads
// none. Whoever serves the instructions of a type serves them only to a caller that reaches the type.
internal sealed class McpInstructions(PluginDbContext<McpPlugin> database, SchemaModel schema, IPermissionChecker permissions)
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<Guid, (string? Guidelines, IReadOnlyDictionary<string, string> ByType)> _sites = [];

    public async ValueTask<string?> GuidelinesAsync(SiteId site, CancellationToken cancellationToken) =>
        (await OfSiteAsync(site, cancellationToken)).Guidelines;

    public async ValueTask<IReadOnlyDictionary<string, string>> ByTypeAsync(SiteId site, CancellationToken cancellationToken) =>
        (await OfSiteAsync(site, cancellationToken)).ByType;

    // An agent of the site may manage and holds some grant there (spec 5.4). A principal whose grants are all in other
    // sites may still name this one, and a Delivery or Preview key reads published content alone; neither is one.
    public static async ValueTask<bool> IsAgentOfAsync(IPermissionChecker permissions, SiteId site, CancellationToken cancellationToken) =>
        await permissions.CanManageAsync(cancellationToken) && await permissions.HoldsGrantInAsync(site, cancellationToken);

    private async ValueTask<(string? Guidelines, IReadOnlyDictionary<string, string> ByType)> OfSiteAsync(SiteId site, CancellationToken cancellationToken)
    {
        if (!_sites.TryGetValue(site.Value, out var instructions))
        {
            instructions = await IsAgentOfAsync(permissions, site, cancellationToken) ? await ReadAsync(site, cancellationToken) : (null, None);
            _sites[site.Value] = instructions;
        }

        return instructions;
    }

    private async Task<(string? Guidelines, IReadOnlyDictionary<string, string> ByType)> ReadAsync(SiteId site, CancellationToken cancellationToken)
    {
        var rows = await database.Set<McpInstruction>().AsNoTracking()
            .Where(instruction => instruction.SiteId == site.Value)
            .Select(instruction => new { instruction.Type, instruction.Text })
            .ToListAsync(cancellationToken);
        return (
            rows.FirstOrDefault(row => row.Type is null)?.Text,
            rows.Where(row => row.Type is not null && schema.FindContentType(row.Type) is not null).ToDictionary(row => row.Type!, row => row.Text, StringComparer.Ordinal));
    }
}
