namespace FluentCMS.Web.Plugins.Contents.IFrame;

public partial class IFrameEditPlugin
{
    private IFrameSettingsModel? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            Plugin.Settings.TryGetValue("Src", out var src);
            Plugin.Settings.TryGetValue("Height", out var height);
            Plugin.Settings.TryGetValue("EmbedCode", out var embedCode);
            Plugin.Settings.TryGetValue("IsUsingSrc", out var isUsingSrc);

            Model = new()
            {
                Src = src ?? string.Empty,
                Height = height ?? string.Empty,
                EmbedCode = embedCode ?? string.Empty,
                IsUsingSrc = isUsingSrc == "true",
            };
        }
        await base.OnInitializedAsync();
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
                { "IsUsingSrc", Model.IsUsingSrc.ToString() },
            }
        };
        await ApiClient.Settings.UpdateAsync(request);

        await OnSubmit.InvokeAsync();
    }

    class IFrameSettingsModel
    {
        public string Src { get; set; } = string.Empty;
        public string Height { get; set; } = string.Empty;
        public string EmbedCode { get; set; } = string.Empty;
        public bool IsUsingSrc { get; set; } = true;
    };
}
