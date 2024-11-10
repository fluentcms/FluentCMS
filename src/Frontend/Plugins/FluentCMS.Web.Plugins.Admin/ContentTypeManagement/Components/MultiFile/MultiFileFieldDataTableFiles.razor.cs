namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiFileFieldDataTableFiles
{
    private Dictionary<Guid, string> FileUrlsDict { get; set; } = [];

    // TODO OnInitialize..
    private string GetFileName(Guid file)
    {
        return "TODO";
    }

    private string GetDownloadUrl(Guid file)
    {
        return "TODO";
    }
}
