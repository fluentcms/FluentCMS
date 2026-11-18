using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Plugins.Media.Admin;

// A part of an asset's detail that saves its own metadata with PATCH /media/{id}: the alt text and the focal point, which
// the field editor of a media field reuses, and the panel's form. A save after the asset changed elsewhere is not sent;
// the part shows the conflict and lets the user take the asset as it is now or save theirs over it.
public abstract class MediaAssetPart : ComponentBase
{
    private JsonObject? pending;
    private MediaAssetInfo? conflict;

    [Parameter, EditorRequired]
    public MediaAssetInfo Asset { get; set; } = default!;

    [Parameter]
    public bool ReadOnly { get; set; }

    // Raised with the asset as it is after a save, or as it is now when the user takes the other change.
    [Parameter]
    public EventCallback<MediaAssetInfo> OnChanged { get; set; }

    [Inject]
    private IManagementApi Api { get; set; } = default!;

    [Inject]
    private protected IStringLocalizer<MediaResources> L { get; set; } = default!;

    protected bool Busy { get; private set; }

    protected string? Error { get; private set; }

    protected bool Conflicted => conflict is not null;

    protected async Task SaveAsync(JsonObject changes, bool overwrite = false)
    {
        if (ReadOnly)
        {
            return;
        }

        (Busy, Error, conflict, pending) = (true, null, null, null);
        try
        {
            var saved = await new MediaApi(Api).UpdateAsync(Asset, changes, overwrite, CancellationToken.None);
            await OnChanged.InvokeAsync(saved);
        }
        catch (MediaConflictException changed)
        {
            (conflict, pending) = (changed.Current, changes);
        }
        catch (ApiProblemException failure)
        {
            Error = MediaRefusal.Text(failure.Problem, L);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            Error = L["Media.Dialog.Failed"];
        }
        finally
        {
            Busy = false;
        }
    }

    // Takes the asset as it is now, leaving the user's change.
    protected async Task TakeTheirsAsync()
    {
        if (conflict is { } current)
        {
            (conflict, pending) = (null, null);
            await OnChanged.InvokeAsync(current);
        }
    }

    // Saves the user's change over the other.
    protected Task KeepMineAsync() => pending is { } changes ? SaveAsync(changes, overwrite: true) : Task.CompletedTask;
}

internal static class MediaRefusal
{
    public static string Text(ApiProblem problem, IStringLocalizer<MediaResources> localizer) => problem switch
    {
        { Status: 403 } => localizer["Media.Dialog.Forbidden"],
        { Errors: [{ Message: { Length: > 0 } message }, ..] } => message,
        { Detail: { Length: > 0 } detail } => detail,
        { Title: { Length: > 0 } title } => title,
        _ => localizer["Media.Dialog.Failed"],
    };
}
