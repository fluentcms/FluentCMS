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

            Model = new()
            {
                Src = src ?? string.Empty,
                Height = height ?? string.Empty,
                EmbedCode = embedCode ?? string.Empty,
                IsUsingSrc = string.IsNullOrEmpty(embedCode),
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
                { "Height", Model.Height },
            }
        };

        if (Model.IsUsingSrc)
        {
            request.Settings.Add("Src", Model.Src);
            request.Settings.Add("EmbedCode", string.Empty);
        }
        else
        {
            request.Settings.Add("Src", string.Empty);
            request.Settings.Add("EmbedCode", Model.EmbedCode);
        }

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
