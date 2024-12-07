using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class TailwindStyleBuilder : IAsyncDisposable
{
    [Inject]
    private ViewState ViewState { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!; 

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<TailwindStyleBuilder>? DotNetRef { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/TailwindStyleBuilder.razor.js");

        var css = await Module.InvokeAsync<string>("initialize", DotNetRef);

        await OnCssGenerated(css);
    }

    private async Task OnCssGenerated(string css) {
        var cssFilePath = Path.Combine("wwwroot", "tailwind", ViewState.Site.Id.ToString(), $"{ViewState.Page.Id}.css");

        var directoryPath = Path.GetDirectoryName(cssFilePath);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(cssFilePath, css);
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
