namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileUploadModal
{
    [Parameter]
    public FileUploadConfiguration Config { get; set; }

    [Parameter]
    public EventCallback<List<FileParameter>> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private List<FileParameter> Files { get; set; }


    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Console.WriteLine($"OnFilesChanged {Config.MaxCount}");
        Files = [];
        foreach (var file in e.GetMultipleFiles(Config.MaxCount))
        {
            Console.WriteLine("file");

            var Data = file.OpenReadStream(Config.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }
        Console.WriteLine("end loop");


        // await GetApiClient<FileClient>().UploadAsync(FolderId, Files);
        // NavigateTo(GetUrl("Files List", new { folderId = FolderId }));

        Console.WriteLine(Files.Select(x => x.FileName).ToList().Count);
        await OnSubmit.InvokeAsync(Files);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}
