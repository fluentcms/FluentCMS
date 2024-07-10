namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiSelectFieldFormCheckboxes
{
    private List<string> Options { get; set; }

    protected override Task OnInitializedAsync()
    {
        Options = Field.Options.Split("\n").Select(x => x.Trim()).ToList();

        return base.OnInitializedAsync();
    }
}
