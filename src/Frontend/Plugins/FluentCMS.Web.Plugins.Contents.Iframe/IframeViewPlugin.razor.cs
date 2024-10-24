namespace FluentCMS.Web.Plugins.Contents.Iframe;

public partial class IframeViewPlugin
{
    private string Source { get; set; }
    private string Height { get; set; }
    private string EmbedCode { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            Plugin.Settings.TryGetValue("Src", out var src);
            Plugin.Settings.TryGetValue("Height", out var height);
            Plugin.Settings.TryGetValue("EmbedCode", out var embedCode);

            Source = src;
            Height = height;
            EmbedCode = embedCode;

        }
    }
}
