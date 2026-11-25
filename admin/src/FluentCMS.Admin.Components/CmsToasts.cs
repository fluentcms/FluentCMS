using FluentCMS.Admin.Components.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FluentCMS.Admin.Components;

// The toasts of ICmsToastService at the end of the theme root, each with its close button. They are no live region, since
// the shell's regions read each message. Written by hand, so that it stays out of the set's public surface.
internal sealed class CmsToasts : ComponentBase, IDisposable
{
    [Inject]
    private CmsToastService Toasts { get; set; } = default!;

    [Inject]
    private ICmsTextSource Texts { get; set; } = default!;

    public void Dispose() => Toasts.Changed -= OnChanged;

    protected override void OnInitialized() => Toasts.Changed += OnChanged;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Toasts.Shown.Count == 0)
        {
            return;
        }

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "cms-toasts");
        foreach (var toast in Toasts.Shown)
        {
            builder.OpenElement(2, "div");
            builder.SetKey(toast);
            builder.AddAttribute(3, "class", "cms-toast");
            builder.AddAttribute(4, "data-intent", toast.Intent.ToString().ToLowerInvariant());
            builder.OpenComponent<CmsIcon>(5);
            builder.AddComponentParameter(6, nameof(CmsIcon.Name), toast.Intent switch
            {
                CmsToastIntent.Success => CmsIconName.Checkmark,
                CmsToastIntent.Info => CmsIconName.Info,
                _ => CmsIconName.Warning,
            });
            builder.CloseComponent();
            builder.OpenElement(7, "span");
            builder.AddAttribute(8, "class", "cms-toast-message");
            builder.AddContent(9, toast.Message);
            builder.CloseElement();
            builder.OpenComponent<CmsButton>(10);
            builder.AddComponentParameter(11, nameof(CmsButton.Appearance), CmsButtonAppearance.Subtle);
            builder.AddComponentParameter(12, nameof(CmsButton.Icon), (CmsIconName?)CmsIconName.Dismiss);
            builder.AddComponentParameter(13, nameof(CmsButton.Label), Texts.Text(CmsStrings.Close));
            builder.AddComponentParameter(14, nameof(CmsButton.OnClick), EventCallback.Factory.Create(this, () => Toasts.Dismiss(toast)));
            builder.CloseComponent();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
