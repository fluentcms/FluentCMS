using FluentCMS.Admin;

namespace FluentCMS.Plugins.AiAssist.Admin;

// What the panel asks the server for. Images are the media ids the user chooses among for altText; TargetLocales the
// locales translate may write in. ForAsset saves an accepted alt text on the asset, and leaves the form's draft as it is.
public sealed record AiSuggestionRequest
{
    public required string Action { get; init; }

    public required string Label { get; init; }

    public string? Type { get; init; }

    public string? Field { get; init; }

    public string? Locale { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<string> TargetLocales { get; init; } = [];

    public IReadOnlyList<Guid> Images { get; init; } = [];

    public bool ForAsset { get; init; }
}

// The panel of a suggestion (spec 5.5), AiSuggestionDialog over the page through the shell's overlays: it streams the
// suggestion and answers the text the user accepted once it has closed, or null when the user declined it, stopped it or
// it failed, so that the draft changes only by an acceptance.
internal sealed class AiSuggestions(IAdminOverlays overlays)
{
    public async Task<string?> AskAsync(AiSuggestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = overlays.Show(builder =>
        {
            builder.OpenComponent<AiSuggestionDialog>(0);
            builder.AddComponentParameter(1, nameof(AiSuggestionDialog.Request), request);
            builder.AddComponentParameter(2, nameof(AiSuggestionDialog.OnDone), (Action<string?>)(accepted => answer.TrySetResult(accepted)));
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
