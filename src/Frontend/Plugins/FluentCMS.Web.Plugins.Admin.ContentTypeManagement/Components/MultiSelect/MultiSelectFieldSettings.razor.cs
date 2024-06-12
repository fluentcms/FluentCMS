namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiSelectFieldSettings
{
    private List<string> Options {
        get => Model.Options.Split("\n").Select(x => x.Trim()).ToList();
    }
}
