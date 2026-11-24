using FluentCMS.Admin.Components.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Components;

/// <summary>
/// The parameters and the open state shared by <see cref="CmsDialog"/> and <see cref="CmsDrawer"/>: a modal dialog named by its
/// title, which takes focus when it opens and gives it back to the control that opened it, also when it is removed from the
/// page while open.
/// </summary>
public abstract class CmsDialogBase : ComponentBase, IHandleEvent, IAsyncDisposable
{
    private bool _shown;
    private bool _appliedOpen;
    private EventCallback<DialogToggleEventArgs>? _onToggle;
    private RenderFragment? _closeTemplate;

    /// <summary>Gets or sets the title, shown at the top and naming the dialog for assistive technology.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the dialog is open.</summary>
    [Parameter]
    public bool Open { get; set; }

    /// <summary>Gets or sets the callback raised when the user closes the dialog, by Escape, outside it or by its close button.</summary>
    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Gets or sets the callback raised once the dialog has closed, by the user or by <see cref="Open"/>.</summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// Gets or sets whether the title shows a close button, which closes the dialog as Escape does. The default is
    /// <see langword="true"/>; a dialog that the user leaves by one of its actions only leaves it out.
    /// </summary>
    [Parameter]
    public bool CloseButton { get; set; } = true;

    /// <summary>Gets or sets the body of the dialog.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Gets or sets the actions at the bottom of the dialog, usually <see cref="CmsButton"/>s.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Inject]
    private ICmsTextSource Texts { get; set; } = default!;

    internal FluentDialog? Dialog { get; set; }

    internal string DialogId { get; } = CmsIds.Next();

    internal RenderFragment TitleTemplate => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "cms-dialog-title");
        builder.AddContent(2, Title);
        builder.CloseElement();
    };

    internal RenderFragment? CloseTemplate => !CloseButton ? null : _closeTemplate ??= builder =>
    {
        builder.OpenComponent<CmsButton>(0);
        builder.AddComponentParameter(1, nameof(CmsButton.Appearance), CmsButtonAppearance.Subtle);
        builder.AddComponentParameter(2, nameof(CmsButton.Icon), (CmsIconName?)CmsIconName.Dismiss);
        builder.AddComponentParameter(3, nameof(CmsButton.Label), Texts.Text(CmsStrings.Close));
        builder.AddComponentParameter(4, nameof(CmsButton.OnClick), EventCallback.Factory.Create(this, CloseAsync));
        builder.CloseComponent();
    };

    // The library raises OnStateChange only for dialogs opened through its service, so the toggle event is handled here.
    internal Dictionary<string, object> DialogAttributes => new(StringComparer.Ordinal)
    {
        ["aria-label"] = Title,
        ["ondialogtoggle"] = _onToggle ??= EventCallback.Factory.Create<DialogToggleEventArgs>(this, OnToggleAsync),
    };

    /// <summary>
    /// Hides a dialog that is removed from the page while open, before its element goes, so that the library gives focus
    /// back to the control that opened it as on any close.
    /// </summary>
    /// <returns>A task that completes once the dialog is hidden.</returns>
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (!_shown || Dialog is null)
        {
            return;
        }

        _shown = false;
        try
        {
            await Dialog.HideAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the dialog with it.
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet() => CmsGuard.Named(Title, GetType().Name, nameof(Title));

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Acts on a change of the parameter only, so that a parent passing Open one-way does not reopen a dismissed dialog.
        if (Dialog is null || Open == _appliedOpen)
        {
            return;
        }

        _appliedOpen = Open;
        if (Open != _shown)
        {
            _shown = Open;
            await (Open ? Dialog.ShowAsync() : Dialog.HideAsync());
        }
    }

    // The toggle of a popover inside the dialog, such as a menu, and the close button change nothing the dialog draws, so
    // they do not render its subtree again; the callbacks they raise render their own receivers.
    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg) => callback.InvokeAsync(arg);

    private Task CloseAsync() => Dialog?.HideAsync() ?? Task.CompletedTask;

    private async Task OnToggleAsync(DialogToggleEventArgs args)
    {
        // Popovers inside the dialog, such as a menu, raise the same event.
        if (args.Id != DialogId || args.NewState != "closed")
        {
            return;
        }

        if (_shown)
        {
            _shown = false;
            await OpenChanged.InvokeAsync(false);
        }

        // The library has given focus back to the opener by now, so a caller that moves it elsewhere does it here.
        await OnClosed.InvokeAsync();
    }
}
