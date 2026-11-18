using FluentCMS.Admin;
using FluentCMS.Admin.Pickers;
using FluentCMS.Admin.Plugins;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Plugins.Media.Admin;

// The media picker other halves and the admin ask for (spec 5.2): MediaPickerDialog shown over the page through the shell's
// overlays, answered when the dialog has closed and given focus back to the control that opened it.
[AdminService(typeof(IMediaPicker))]
public sealed class MediaPicker(IAdminOverlays overlays) : IMediaPicker
{
    public async Task<IReadOnlyList<MediaPickerItem>?> PickAsync(MediaPickerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var answer = new TaskCompletionSource<IReadOnlyList<MediaPickerItem>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = overlays.Show(builder =>
        {
            builder.OpenComponent<MediaPickerDialog>(0);
            builder.AddComponentParameter(1, nameof(MediaPickerDialog.Request), request);
            builder.AddComponentParameter(2, nameof(MediaPickerDialog.OnDone), (Action<IReadOnlyList<MediaPickerItem>?>)(items => answer.TrySetResult(items)));
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
