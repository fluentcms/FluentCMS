using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

public sealed record McpInstructionRequest(string? Text);

// Type is null for the guidelines of the site. Orphaned says that the type is no longer in the schema, so the row is
// served to no agent and is listed to be removed.
public sealed record McpInstructionInfo(string? Type, string Text, bool Orphaned, DateTimeOffset UpdatedAt, Guid UpdatedBy);

// The guidelines and the type instructions of the site of the current operation. Every call needs mcp.manage in that
// site. The text is data that an agent is shown, and is never run.
internal sealed class McpInstructionService(
    PluginDbContext<McpPlugin> database,
    IPermissionChecker permissions,
    IOperationContextAccessor operations,
    IEventBus events,
    SchemaModel schema,
    TimeProvider time,
    ILogger<McpInstructionService> logger)
{
    // The guidelines go into the handshake and the instructions of a type into the descriptions of its tools, so both
    // are kept short enough to leave a model its context.
    public const int TextMaxLength = 4000;

    public async Task<IReadOnlyList<McpInstructionInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        var rows = await database.Set<McpInstruction>().AsNoTracking().Where(instruction => instruction.SiteId == site.Value).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.Type, StringComparer.Ordinal).Select(Info)];
    }

    // The whole text, for the guidelines when the type is null. A type must be in the schema to be written to.
    public async Task<McpInstructionInfo> SaveAsync(string? type, McpInstructionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var site = await RequireAsync(cancellationToken);
        if (type is not null && schema.FindContentType(type) is null)
        {
            throw new PluginException(McpErrorCodes.InstructionTypeNotFound, $"The schema has no content type '{type}'.");
        }

        var text = TextOf(request.Text);
        var now = time.GetUtcNow();
        var actor = Operation().AuditPrincipal.Id;
        var row = await Rows(site, type).FirstOrDefaultAsync(cancellationToken);
        var added = row is null;
        if (row is null)
        {
            row = new McpInstruction { Id = Guid.CreateVersion7(), SiteId = site.Value, Type = type };
            database.Add(row);
        }

        row.Text = text;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (added)
        {
            // Two writers that both found no row meet at the unique index, and the later one writes over the row that is there.
            database.Entry(row).State = EntityState.Detached;
            var written = await Rows(site, type).ExecuteUpdateAsync(
                set => set.SetProperty(item => item.Text, text).SetProperty(item => item.UpdatedAt, now).SetProperty(item => item.UpdatedBy, actor),
                CancellationToken.None);
            if (written == 0)
            {
                throw;
            }
        }

        McpLog.InstructionChanged(logger, "saved", type, site.Value);
        await events.PublishAsync(new McpInstructionSavedEvent(type), cancellationToken);
        return Info(row);
    }

    // A row of a type that is no longer in the schema is deleted like any other.
    public async Task DeleteAsync(string? type, CancellationToken cancellationToken)
    {
        var site = await RequireAsync(cancellationToken);
        if (await Rows(site, type).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new PluginException(McpErrorCodes.InstructionNotFound, type is null ? "The site has no guidelines." : $"The site has no instructions for the type '{type}'.");
        }

        McpLog.InstructionChanged(logger, "deleted", type, site.Value);
        await events.PublishAsync(new McpInstructionDeletedEvent(type), cancellationToken);
    }

    private static string TextOf(string? given)
    {
        var text = given?.Trim();
        var problem = string.IsNullOrEmpty(text) ? "A text is required; delete the instructions to have none."
            : text.Length > TextMaxLength ? $"The text has at most {TextMaxLength} characters."
            : text.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')) ? "The text holds a control character other than a line break or a tab."
            : null;
        return problem is null ? text! : throw new ValidationException([new ValidationError("text", McpErrorCodes.InvalidInstruction, problem)]);
    }

    private IQueryable<McpInstruction> Rows(SiteId site, string? type) =>
        database.Set<McpInstruction>().Where(instruction => instruction.SiteId == site.Value && instruction.Type == type);

    private OperationContext Operation() =>
        operations.Current ?? throw new InvalidOperationException("The agent instructions of a site are managed inside an operation, which names the site.");

    private async Task<SiteId> RequireAsync(CancellationToken cancellationToken)
    {
        var site = Operation().Site;
        await permissions.RequireAsync(McpPermissions.Manage, site, cancellationToken);
        return site;
    }

    private McpInstructionInfo Info(McpInstruction row) =>
        new(row.Type, row.Text, row.Type is not null && schema.FindContentType(row.Type) is null, row.UpdatedAt, row.UpdatedBy);
}
