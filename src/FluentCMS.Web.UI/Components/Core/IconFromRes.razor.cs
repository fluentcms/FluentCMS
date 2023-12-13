using FluentCMS.Web.UI.Resources;
using Microsoft.AspNetCore.Components;
using System.Resources;

namespace FluentCMS.Web.UI.Components.Core;
public partial class IconFromRes
{
    [Inject(Key = "FluentCMS.Web.UI.Resources.Icons")]
    public ResourceManager? ResourceManager { get; set; } = null;
    public MarkupString IconSvgContent { get; set; } = default!;
    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (ResourceManager != null)
        {
            IconSvgContent = (MarkupString)(ResourceManager?.GetString(IconDefinition.IconName ?? "") ?? "");
        }


    }
    [Parameter]
    [EditorRequired]
    public required IconDefinition IconDefinition { get; set; }
}
