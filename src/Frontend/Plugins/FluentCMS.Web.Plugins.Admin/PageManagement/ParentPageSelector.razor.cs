namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class ParentPageSelector
{
    [Parameter]
    public Guid? Value { get; set; } = Guid.Empty;

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter]
    public List<PageDetailResponse> Pages { get; set; } = [];

    private List<TreeSelectorItemType> PageOptions = []; 
    private string ParentPageId { get; set; } = Guid.Empty.ToString();

    async Task GetPageOptions()
    {
        PageOptions = new List<TreeSelectorItemType>
        {
            new TreeSelectorItemType
            {
                // Icon = "📁",
                Key = Guid.Empty.ToString(),
                Text = "(none)",
                Items = new List<TreeSelectorItemType>()
            }
        };

        var pageDictionary = new Dictionary<Guid, TreeSelectorItemType>();
        
        foreach (var page in Pages)
        {
            var pageItem = new TreeSelectorItemType
            {
                // Icon = "📄",
                Key = page.Id.ToString(),
                Text = page.Title,
                Items = new List<TreeSelectorItemType>()
            };

            pageDictionary[page.Id] = pageItem;
        }

        foreach (var page in Pages)
        {
            Guid? parentId = page.ParentId == Guid.Empty ? (Guid?)null : page.ParentId;

            if (!parentId.HasValue)
            {
                PageOptions.Add(pageDictionary[page.Id]);
            }
            else if (pageDictionary.ContainsKey(parentId.Value))
            {
                pageDictionary[parentId.Value].Items.Add(pageDictionary[page.Id]);
            }
        }
    }

    async Task OnChange()
    {
        if(Guid.TryParse(ParentPageId, out var parentId)) {
            Value = parentId;
            ValueChanged.InvokeAsync(Value);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await GetPageOptions();
        ParentPageId = Value?.ToString() ?? Guid.Empty.ToString();
    }
}