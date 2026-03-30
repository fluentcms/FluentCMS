using Ganss.Xss;

namespace FluentCMS.Web.Plugins.TextHTML;

public partial class TextHTMLViewPlugin
{
    private const string CONTENT_TYPE_NAME = nameof(TextHTMLContent);
    private TextHTMLContent? Item { get; set; }
    private static readonly HtmlSanitizer Sanitizer = new();

    private string _sanitizedContent = string.Empty;

    private async Task UpdateContent(string content)
    {
        if (Item is null)
            return;

        Item.Content = content;
        _sanitizedContent = Sanitizer.Sanitize(Item.Content);
        await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Item.Id, Item.ToDictionary());
    }

    private async Task OnCancel()
    {
        StateHasChanged();
        await Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Count != 0)
            {
                Item = response.Data.ToContentList<TextHTMLContent>().FirstOrDefault() ?? default!;
                _sanitizedContent = Item is not null ? Sanitizer.Sanitize(Item.Content) : string.Empty;
            }
        }
    }
}
