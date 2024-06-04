using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI;

public partial class PluginsSection
{
    [Parameter]
    // this will be set while dynamically rendering the template
    public string Name { get; set; } = default!;

    [CascadingParameter]
    private PageFullDetailResponse? Page { get; set; }
}
