namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class ParentPageSelector
{
    [Parameter]
    public Guid? Value { get; set; } = Guid.Empty;

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public List<PageDetailResponse> Pages { get; set; } = [];

    private List<TreeSelectorItemType> PageOptions { get; set; } = [];

    private string ParentPageId { get; set; } = Guid.Empty.ToString();

    private async Task GetPageOptions()
    {
        PageOptions =
        [
            new() {
                // Icon = "📁",
                Key = Guid.Empty.ToString(),
                Text = "(none)",
                Items = []
            }
        ];

        var pageDictionary = new Dictionary<Guid, TreeSelectorItemType>();

        foreach (var page in Pages)
        {
            var pageItem = new TreeSelectorItemType
            {
                // Icon = "📄",
                Key = page.Id.ToString(),
                Text = page.Title ?? string.Empty,
                Items = []
            };

            pageDictionary[page.Id] = pageItem;
        }

        foreach (var page in Pages)
        {
            Guid? parentId = page.ParentId == Guid.Empty ? null : page.ParentId;

            if (!parentId.HasValue)
            {
                PageOptions.Add(pageDictionary[page.Id]);
            }
            else if (pageDictionary.TryGetValue(parentId.Value, out TreeSelectorItemType? value))
            {
                value.Items.Add(pageDictionary[page.Id]);
            }
        }
        await Task.CompletedTask;
    }

    private async Task OnChange()
    {
        if (Guid.TryParse(ParentPageId, out var parentId))
        {
            Value = parentId;
            await ValueChanged.InvokeAsync(Value);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await GetPageOptions();
        ParentPageId = Value?.ToString() ?? Guid.Empty.ToString();
    }
}
