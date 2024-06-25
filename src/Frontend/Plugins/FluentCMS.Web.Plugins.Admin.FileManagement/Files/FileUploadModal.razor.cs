namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileUploadModal
{
    [Parameter]
    public FileUploadConfiguration Config { get; set; }

    [Parameter]
    public EventCallback<List<FileParameter>> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private string Model { get; set; } = string.Empty;

    private List<FileParameter> Files { get; set; }

    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Files = [];
        foreach (var file in e.GetMultipleFiles(Config.MaxCount))
        {
            var Data = file.OpenReadStream(Config.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }
        await OnSubmit.InvokeAsync(Files);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Files);
    }
}
