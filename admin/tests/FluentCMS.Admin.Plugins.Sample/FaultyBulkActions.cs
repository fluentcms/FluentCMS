using FluentCMS.Admin.Actions;

namespace FluentCMS.Admin.Plugins.Sample;

// Bulk actions that misbehave, which the half does not declare: a test's own manifest names them, so that the sample's
// manifest stays as its attributes say.
public sealed class ThrowingBulkAction : BulkActionBase
{
    public override string Label => "Throw";

    public override Task RunAsync(BulkActionContext context, IBulkProgress progress, CancellationToken cancellationToken)
    {
        progress.Report(new BulkItemResult { Id = context.Ids[0], Status = "approved" });
        throw new InvalidOperationException("The sample's action failed.");
    }
}

// Reports one entry, then waits forever, whatever its token says.
public sealed class HangingBulkAction : BulkActionBase
{
    public override string Label => "Hang";

    public override async Task RunAsync(BulkActionContext context, IBulkProgress progress, CancellationToken cancellationToken)
    {
        progress.Report(new BulkItemResult { Id = context.Ids[0], Status = "approved" });
        await Task.Delay(Timeout.Infinite, CancellationToken.None);
    }
}
