namespace FluentCMS.Web.UI.ComponentsDocs.Components;

public partial class Sidebar
{
    [Parameter]
    public RenderFragment? Header { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public RenderFragment? Footer { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Secondary { get; set; }

    [Parameter]
    public string? Title { get; set; }

    public string Class => $"h-screen transition-transform border-r border-gray-200 dark:border-gray-700 min-h-full flex flex-col overflow-y-auto -translate-x-full lg:!translate-x-0 {(Secondary ? "" : "fixed top-0 left-0 z-40 w-64 pt-14 lg:pt-0 bg-white dark:bg-gray-800")}";

    public string MainClass => $"py-5 px-3 grow {(Secondary ? "w-52 pt-0" : "bg-white dark:bg-gray-800")}";
}
