using FluentCMS.Web.UI.Resources;
using Microsoft.AspNetCore.Components;
using System.Resources;

namespace FluentCMS.Web.UI.Components.Core;
public partial class IconFromRes
{
    [Inject(Key = "FluentCMS.Web.UI.Resources.Icons")]
    public required ResourceManager ResourceManager { get; set; }
    public MarkupString IconSvgContent { get; set; } = default!;
    protected override void OnInitialized()
    {
        base.OnInitialized();
        IconSvgContent = (MarkupString)(ResourceManager.GetString(IconDefinition.IconName) ?? throw new Exception("Invalid IconDefinition"));

    }
    [Parameter]
    [EditorRequired]
    public required IconDefinition IconDefinition { get; set; }
}
