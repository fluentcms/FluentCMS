namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldFormAutocomplete
{
    private List<string> Items { get; set; }

    protected override Task OnInitializedAsync()
    {
        Items = Field.Options.Split("\n").Select(x => x.Trim()).ToList();

        return base.OnInitializedAsync();
    }
}
