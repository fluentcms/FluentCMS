using FluentCMS.Admin;

namespace FluentCMS.Plugins.AiAssist.Admin;

// What the panel asks the server for. Images are the media ids the user chooses among for altText, of the entry DocumentId
// names for a field, and Targets the locales translate offers. ForAsset saves an accepted alt text on the asset, and leaves
// the form's draft as it is.
public sealed record AiSuggestionRequest
{
    public required string Action { get; init; }

    public required string Label { get; init; }

    public string? Type { get; init; }

    public string? Field { get; init; }

    public string? Locale { get; init; }

    public string? Text { get; init; }

    public Guid? DocumentId { get; init; }

    public IReadOnlyList<Guid> Images { get; init; } = [];

    public IReadOnlyList<AiTargetLocale> Targets { get; init; } = [];

    public bool ForAsset { get; init; }
}

// A locale translate may write into, with the name the panel shows for it.
public sealed record AiTargetLocale(string Code, string Name);

// The text the user accepted, and for translate the locale it is for.
public sealed record AiAccepted(string Text, string? Locale);

// The panel of a suggestion (spec 5.5), AiSuggestionDialog over the page through the shell's overlays: it streams the
// suggestion and answers what the user accepted once it has closed, or null when the user declined it, stopped it or it
// failed, so that the draft changes only by an acceptance.
internal sealed class AiSuggestions(IAdminOverlays overlays)
{
    public async Task<AiAccepted?> AskAsync(AiSuggestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var answer = new TaskCompletionSource<AiAccepted?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = overlays.Show(builder =>
        {
            builder.OpenComponent<AiSuggestionDialog>(0);
            builder.AddComponentParameter(1, nameof(AiSuggestionDialog.Request), request);
            builder.AddComponentParameter(2, nameof(AiSuggestionDialog.OnDone), (Action<AiAccepted?>)(accepted => answer.TrySetResult(accepted)));
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
