namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [new(nameof(DateFieldFormInput), "Date Input")];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(DateFieldDataTableView), "Date String")];
    }

    private List<DateFormatOptions> DateFormats {
        get => [
            new("MM/dd/yyyy", DateTime.Now.ToString("MM/dd/yyyy")),
            new("dddd, dd MMMM yyyy", DateTime.Now.ToString("dddd, dd MMMM yyyy")),
            new("dddd, dd MMMM yyyy", DateTime.Now.ToString("dddd, dd MMMM yyyy")),
            new("dddd, dd MMMM yyyy", DateTime.Now.ToString("dddd, dd MMMM yyyy")),
            new("MM/dd/yyyy HH:mm", DateTime.Now.ToString("MM/dd/yyyy HH:mm")),
            new("MM/dd/yyyy hh:mm tt", DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")),
            new("MMMM dd", DateTime.Now.ToString("MMMM dd")),
            new("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK")),
            new("ddd, dd MMM yyy HH':'mm':'ss 'GMT'", DateTime.Now.ToString("ddd, dd MMM yyy HH':'mm':'ss 'GMT'")),
            new("yyyy'-'MM'-'dd'T'HH':'mm':'ss", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss")),
            new("HH:mm", DateTime.Now.ToString("HH:mm")),
            new("hh:mm tt", DateTime.Now.ToString("hh:mm tt")),
            new("H:mm", DateTime.Now.ToString("H:mm")),
            new("h:mm tt", DateTime.Now.ToString("h:mm tt")),
            new("HH:mm:ss", DateTime.Now.ToString("HH:mm:ss")),
            new("yyyy MMMM", DateTime.Now.ToString("yyyy MMMM")),
        ];
    }

    protected override async Task OnInitializedAsync() 
    {
        DateFormats.Select(x => {
            Console.WriteLine($"{x.Key}: {x.Text}");
            return x;
        });
    }
}
