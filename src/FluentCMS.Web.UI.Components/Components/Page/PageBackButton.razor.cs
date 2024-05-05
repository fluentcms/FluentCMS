namespace FluentCMS.Web.UI.Components;

public partial class PageBackButton
{
    [Inject]
    IJSRuntime JsRuntime { get; set; }

    public async Task GoBack()
    {
        await JsRuntime.InvokeAsync<object>("history.back");
    }
}


