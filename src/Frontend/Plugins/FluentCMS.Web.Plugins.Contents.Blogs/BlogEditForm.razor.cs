namespace FluentCMS.Web.Plugins.Contents.Blogs;

public partial class BlogEditForm
{
    [Parameter]
    public BlogContent Model { get; set; }

    [Parameter]
    public string FormName { get; set; }

    [Parameter]
    public EventCallback<BlogContent> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

}