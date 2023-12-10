using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public class BaseAppComponent : ComponentBase
{
    [CascadingParameter]
    public AppState AppState { get; set; } = default!;
}
