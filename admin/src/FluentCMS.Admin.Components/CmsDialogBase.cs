using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace FluentCMS.Admin.Components;

/// <summary>
/// The parameters and the open state shared by <see cref="CmsDialog"/> and <see cref="CmsDrawer"/>: a modal dialog named by its
/// title, which takes focus when it opens and gives it back to the control that opened it.
/// </summary>
public abstract class CmsDialogBase : ComponentBase
{
    private bool _shown;
    private bool _appliedOpen;
    private EventCallback<DialogToggleEventArgs>? _onToggle;

    /// <summary>Gets or sets the title, shown at the top and naming the dialog for assistive technology.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the dialog is open.</summary>
    [Parameter]
    public bool Open { get; set; }

    /// <summary>Gets or sets the callback raised when the user closes the dialog, by Escape or outside it.</summary>
    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Gets or sets the body of the dialog.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Gets or sets the actions at the bottom of the dialog, usually <see cref="CmsButton"/>s.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    internal FluentDialog? Dialog { get; set; }

    internal string DialogId { get; } = CmsIds.Next();

    internal RenderFragment TitleTemplate => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "cms-dialog-title");
        builder.AddContent(2, Title);
        builder.CloseElement();
    };

    // The library raises OnStateChange only for dialogs opened through its service, so the toggle event is handled here.
    internal Dictionary<string, object> DialogAttributes => new(StringComparer.Ordinal)
    {
        ["aria-label"] = Title,
        ["ondialogtoggle"] = _onToggle ??= EventCallback.Factory.Create<DialogToggleEventArgs>(this, OnToggleAsync),
    };

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

    private async Task OnToggleAsync(DialogToggleEventArgs args)
    {
        // Popovers inside the dialog, such as a menu, raise the same event.
        if (args.Id == DialogId && args.NewState == "closed" && _shown)
        {
            _shown = false;
            await OpenChanged.InvokeAsync(false);
        }
    }
}
