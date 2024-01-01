using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public class BasePluginComponent : BaseSiteComponent
{
    [Parameter]
    public PluginDetailResponse Plugin { get; set; } = default!;
}
