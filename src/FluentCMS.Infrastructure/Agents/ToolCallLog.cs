using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Infrastructure.Agents;

// A scope and so a DbContext of its own for every record, so recording never joins a caller's unit of work.
internal sealed class ToolCallLog(IServiceScopeFactory scopes) : IToolCallLog
{
    public async Task AppendAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        context.ToolCalls.Add(ToolCallRow.From(call));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FinishAsync(Guid callId, ToolCallOutcome outcome, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().ToolCalls
            .Where(row => row.Id == callId && row.Outcome == ToolCallOutcome.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Outcome, outcome).SetProperty(row => row.Duration, duration), cancellationToken);
    }
}
