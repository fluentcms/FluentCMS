namespace FluentCMS.Web.Plugins.Contents.Iframe;

public partial class IframeEditPlugin
{
    private IframeSettingsModel? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            Plugin.Settings.TryGetValue("Src", out var src);
            Plugin.Settings.TryGetValue("Height", out var height);
            Plugin.Settings.TryGetValue("EmbedCode", out var embedCode);

            Model = new()
            {
                Src = src ?? string.Empty,
                Height = height ?? string.Empty,
                EmbedCode = embedCode ?? string.Empty,
            };
        }
    }

    private async Task HandleSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        var request = new SettingsUpdateRequest
        {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string> {
                { "Src", Model.Src },
                { "Height", Model.Height },
                { "EmbedCode", Model.EmbedCode },
            }
        };
        await ApiClient.Settings.UpdateAsync(request);

        await OnSubmit.InvokeAsync();
    }

    class IframeSettingsModel
    {
        public string Src { get; set; } = string.Empty;
        public string Height { get; set; } = string.Empty;
        public string EmbedCode { get; set; } = string.Empty;
    };
}
