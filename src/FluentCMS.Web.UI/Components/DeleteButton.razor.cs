namespace FluentCMS.Web.UI.Plugins.Components;

public partial class DeleteButton
{
    private bool confirmOpen { get; set; } = false;

    [Parameter]
    public EventCallback<Guid> OnDelete { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [Parameter]
    public string Message { get; set; } = "Are you sure to remove this Item?";

    public async Task HandleClick() {
        confirmOpen = true;
    }

    public async Task HandleConfirm() {
        Console.WriteLine($"HandleConfirm {Id}");
        OnDelete.InvokeAsync(Id);        
    }
}
