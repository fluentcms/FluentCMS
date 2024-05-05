using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class CardHeader
{
    [Parameter]
    public string? Title { get; set; }
}