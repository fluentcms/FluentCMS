namespace FluentCMS.Web.UI.Components;

public class ToastModel
{
    public int Duration { get; set; }

    public string? Message { get; set; }

    public ToastType Type { get; set; }
}
