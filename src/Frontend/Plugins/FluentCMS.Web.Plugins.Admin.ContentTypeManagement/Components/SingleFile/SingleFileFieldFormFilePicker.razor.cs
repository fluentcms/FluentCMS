namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldFormFilePicker
{
    private bool FilesModalOpen { get; set; }
    
    public async Task OnChooseClicked()
    {
        FilesModalOpen = true;
    }
    public async Task OnFileChoose(Guid id)
    {
        Console.WriteLine("File Chosen: " + id.ToString());
    }
}