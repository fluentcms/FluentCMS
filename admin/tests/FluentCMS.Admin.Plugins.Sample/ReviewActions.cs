using System.Text.Json.Nodes;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Pickers;

namespace FluentCMS.Admin.Plugins.Sample;

[EntryAction("approve", ContentTypes = ["review"], Permission = "reviews.notes.manage")]
public sealed class ApproveAction(ReviewsApi api) : EntryActionBase
{
    public override string Label => "Approve";

    public override string? ConfirmationText(EntryActionContext context) => $"Approve {context.Entry.Id}?";

    public override Task RunAsync(EntryActionContext context, CancellationToken cancellationToken)
    {
        api.Approved++;
        return Task.CompletedTask;
    }
}

[BulkAction("approveAll", ContentTypes = ["review", "note"], Permission = "reviews.notes.manage")]
public sealed class ApproveAllAction(ReviewsApi api) : BulkActionBase
{
    public override string Label => "Approve";

    public override Task RunAsync(BulkActionContext context, IBulkProgress progress, CancellationToken cancellationToken)
    {
        foreach (var id in context.Ids)
        {
            api.Approved++;
            progress.Report(new BulkItemResult { Id = id, Status = "approved" });
        }

        return Task.CompletedTask;
    }
}

[FieldAction("shout", FieldTypes = ["text", "longText"])]
public sealed class ShoutAction : FieldActionBase
{
    public override string Label => "Shout";

    public override string? Icon => "edit";

    public override Task<FieldActionResult> RunAsync(FieldActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Value is JsonValue value && value.TryGetValue<string>(out var text)
            ? FieldActionResult.Replace(JsonValue.Create(text.ToUpperInvariant()))
            : FieldActionResult.Unchanged);
}

[AdminService(typeof(IMediaPicker))]
public sealed class SampleMediaPicker : IMediaPicker
{
    public Task<IReadOnlyList<MediaPickerItem>?> PickAsync(MediaPickerRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MediaPickerItem>?>(null);
}
