namespace FluentCMS.Web.UI;

public partial class AdminSidebar
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;
    
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    private List<ContentTypeDetailResponse> ContentTypes { get; set; } = [];

    private string CurrentPath { get; set; } = string.Empty;

    private string ActiveItemClasses = "bg-primary-100 dark:bg-gray-700 text-primary-900 dark:text-gray-100"; 
    private string ActiveIconClasses = "text-primary-900 dark:text-gray-100"; 
    
    protected override async Task OnInitializedAsync()
    {
        CurrentPath = new Uri(NavigationManager.Uri).LocalPath;
        var response = await ApiClient.ContentType.GetAllAsync();

        if(response?.Data != null)
        {
            ContentTypes = response.Data.ToList();
        }
    }
}