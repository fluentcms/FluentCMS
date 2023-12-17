using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public class BasePluginComponent : BaseAppComponent
{
    [Parameter]
    public PluginResponse Plugin { get; set; } = default!;

}
