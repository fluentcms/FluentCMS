namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldFormSelect
{
    private List<string> SelectItems { get; set; }

    protected override Task OnInitializedAsync()
    {
        SelectItems = Field.Options.Split("\n").Select(x => x.Trim()).ToList();

        return base.OnInitializedAsync();
    }
}
