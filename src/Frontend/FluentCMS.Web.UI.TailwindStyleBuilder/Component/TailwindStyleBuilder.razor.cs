using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI.TailwindStyleBuilder;

public partial class TailwindStyleBuilder : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public EventCallback<string> OnCssGenerated { get; set; } = default!;

    [Parameter]
    public string Config { get; set; } = "{}";

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<TailwindStyleBuilder>? DotNetRef { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.TailwindStyleBuilder/TailwindStyleBuilder.js");

        var css = await Module.InvokeAsync<string>("initialize", DotNetRef, Config);

        await OnCssGenerated.InvokeAsync(css);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Module is not null)
            {
                await Module.DisposeAsync();
            }

            if (DotNetRef != null)
                DotNetRef.Dispose();
        } 
        catch(Exception ex)
        {
            // 
        }        
    }

}
