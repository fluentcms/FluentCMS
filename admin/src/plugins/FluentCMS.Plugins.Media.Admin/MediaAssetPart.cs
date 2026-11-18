using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Plugins.Media.Admin;

// A part of an asset's detail that saves its own metadata with PATCH /media/{id}: the alt text and the focal point, which
// the field editor of a media field reuses, and the panel's form. A save after the asset changed elsewhere is not sent;
// the part shows the conflict and lets the user take the asset as it is now or save theirs over it. The saves of the parts
// that share a MediaSaveGate, as those of the panel do, go one after another, each based on the answer of the one before;
// changes made while a save of the part waits join it.
public abstract class MediaAssetPart : ComponentBase
{
    private readonly MediaSaveGate own = new();
    private JsonObject? queued;
    private bool queuedOverwrite;
    private Task queuedSave = Task.CompletedTask;
    private int sending;
    private JsonObject? pending;
    private MediaAssetInfo? conflict;
    private bool reset;

    [Parameter, EditorRequired]
    public MediaAssetInfo Asset { get; set; } = default!;

    [Parameter]
    public bool ReadOnly { get; set; }

    // Raised with the asset as it is after a save, or as it is now when the user takes the other change.
    [Parameter]
    public EventCallback<MediaAssetInfo> OnChanged { get; set; }

    [CascadingParameter]
    private MediaSaveGate? Gate { get; set; }

    [Inject]
    private IManagementApi Api { get; set; } = default!;

    [Inject]
    private protected IStringLocalizer<MediaResources> L { get; set; } = default!;

    protected bool Busy => sending > 0 || queued is not null;

    protected string? Error { get; private set; }

    protected bool Conflicted => conflict is not null;

    // What a save or a conflict of one asset left is not shown with another.
    public override Task SetParametersAsync(ParameterView parameters)
    {
        var before = Asset?.Id;
        parameters.SetParameterProperties(this);
        if (before is not null && before != Asset?.Id)
        {
            (Error, conflict, pending) = (null, null, null);
        }

        return base.SetParametersAsync(ParameterView.Empty);
    }

    // Whether the user took the other change since the last call, so that the part shows the asset's values instead of its own.
    protected bool TakeReset()
    {
        var taken = reset;
        reset = false;
        return taken;
    }

    protected Task SaveAsync(JsonObject changes, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (ReadOnly)
        {
            return Task.CompletedTask;
        }

        if (queued is not null)
        {
            foreach (var (name, value) in changes)
            {
                queued[name] = value?.DeepClone();
            }

            queuedOverwrite |= overwrite;
            return queuedSave;
        }

        (queued, queuedOverwrite) = ((JsonObject)changes.DeepClone(), overwrite);
        return queuedSave = SendAsync();
    }

    private async Task SendAsync()
    {
        var gate = Gate ?? own;
        var leave = await gate.EnterAsync();
        var (changes, overwrite) = (queued!, queuedOverwrite);
        queued = null;
        sending++;
        var basis = gate.Basis(Asset);
        (Error, conflict, pending) = (null, null, null);
        try
        {
            var saved = await new MediaApi(Api).UpdateAsync(basis, changes, overwrite, CancellationToken.None);
            gate.Latest = saved;
            await OnChanged.InvokeAsync(saved);
        }
        catch (MediaConflictException changed)
        {
            if (changed.Current.Id == Asset.Id)
            {
                (conflict, pending) = (changed.Current, changes);
            }
        }
        catch (ApiProblemException failure)
        {
            Error = basis.Id == Asset.Id ? MediaRefusal.Text(failure.Problem, L) : null;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            Error = basis.Id == Asset.Id ? L["Media.Dialog.Failed"].Value : null;
        }
        finally
        {
            sending--;
            leave();
        }
    }

    // Takes the asset as it is now: the part shows its values and leaves the user's change.
    protected async Task TakeTheirsAsync()
    {
        if (conflict is { } current)
        {
            (conflict, pending, reset) = (null, null, true);
            await OnChanged.InvokeAsync(current);
        }
    }

    // Saves the user's change over the other.
    protected Task KeepMineAsync() => pending is { } changes ? SaveAsync(changes, overwrite: true) : Task.CompletedTask;
}

// The saves of one panel, one at a time; each is based on the newest of the asset its part shows and the last answer. A
// component runs on the renderer's one thread, so a chain of tasks orders them, which keeps a semaphore out of the app.
internal sealed class MediaSaveGate
{
    private Task tail = Task.CompletedTask;

    // Waits for the saves before this one and answers what ends it.
    public async Task<Action> EnterAsync()
    {
        var before = tail;
        var done = new TaskCompletionSource();
        tail = done.Task;
        await before;
        return done.SetResult;
    }

    public MediaAssetInfo? Latest { get; set; }

    public MediaAssetInfo Basis(MediaAssetInfo shown) =>
        Latest is { } latest && latest.Id == shown.Id && latest.Locale == shown.Locale && latest.UpdatedAt > shown.UpdatedAt ? latest : shown;
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
