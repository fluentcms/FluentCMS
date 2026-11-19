using FluentCMS.Admin.Api;
using FluentCMS.Admin.Pickers;

namespace FluentCMS.Admin.Content;

// The relation picker as a service (spec 4.9), for a component that has no relation field to draw it, such as the entry links
// of rich text: RelationPicker shown over the page through the shell's overlays, as the media half shows its picker. A type the
// schema does not have, which the caller may not read, has no picker and answers null.
internal sealed class RelationPickerService(IAdminOverlays overlays, SiteMetadata metadata) : IRelationPicker
{
    public async Task<IReadOnlyList<ContentEntry>?> PickAsync(RelationPickerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var schema = await metadata.GetSchemaAsync(cancellationToken);
        if (schema.Types.FirstOrDefault(type => type.Name == request.Type) is not { } target)
        {
            return null;
        }

        var answer = new TaskCompletionSource<IReadOnlyList<ContentEntry>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = overlays.Show(builder =>
        {
            builder.OpenComponent<RelationPickerOverlay>(0);
            builder.AddComponentParameter(1, nameof(RelationPickerOverlay.Target), target);
            builder.AddComponentParameter(2, nameof(RelationPickerOverlay.Request), request);
            builder.AddComponentParameter(3, nameof(RelationPickerOverlay.OnDone), (Action<IReadOnlyList<ContentEntry>?>)(entries => answer.TrySetResult(entries)));
            builder.CloseComponent();
        });

        try
        {
            using (cancellationToken.Register(() => answer.TrySetResult(null)))
            {
                return await answer.Task;
            }
        }
        finally
        {
            shown.Dispose();
        }
    }
}
