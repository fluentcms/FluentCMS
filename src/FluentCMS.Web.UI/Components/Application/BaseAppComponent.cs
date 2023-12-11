using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public class BaseAppComponent : ComponentBase
{
    [Inject]
    public AppState AppState { get; set; } = default!;
}
