namespace FluentCMS.Web.Plugins.IFrame;

public partial class IFrameViewPlugin
{
    private string Source { get; set; } = string.Empty;
    private string Height { get; set; } = string.Empty;
    private string EmbedCode { get; set; } = string.Empty;
    private bool IsUsingSrc { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            Plugin.Settings.TryGetValue("Src", out var src);
            Plugin.Settings.TryGetValue("Height", out var height);
            Plugin.Settings.TryGetValue("EmbedCode", out var embedCode);

            Source = src ?? string.Empty;
            Height = height ?? string.Empty;
            EmbedCode = embedCode ?? string.Empty;
            IsUsingSrc = string.IsNullOrEmpty(embedCode);
        }
        await base.OnInitializedAsync();
    }
}
