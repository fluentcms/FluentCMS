using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public class BasePluginComponent : BaseAppComponent
{
    [CascadingParameter(Name = "Plugin")]
    public PluginResponse? Plugin { get; set; }

}
