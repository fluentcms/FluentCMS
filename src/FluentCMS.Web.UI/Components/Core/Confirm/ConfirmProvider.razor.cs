using Microsoft.AspNetCore.Components;

public partial class ConfirmProvider
{
    [Parameter]
    public string Message { get; set; } = "aaaaaa";

    public void Info(string Message) {
    }
}
