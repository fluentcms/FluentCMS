namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldSettings
{
    DateTime ExampleDate = DateTime.Parse("2024-06-12T19:23:40");
    private List<DateFormatOptions> DateFormats {
        get => [
            new("MM/dd/yyyy", ExampleDate.ToString("MM/dd/yyyy")),
            new("dddd, dd MMMM yyyy", ExampleDate.ToString("dddd, dd MMMM yyyy")),
            new("dddd, dd MMMM yyyy", ExampleDate.ToString("dddd, dd MMMM yyyy")),
            new("dddd, dd MMMM yyyy", ExampleDate.ToString("dddd, dd MMMM yyyy")),
            new("MM/dd/yyyy HH:mm", ExampleDate.ToString("MM/dd/yyyy HH:mm")),
            new("MM/dd/yyyy hh:mm tt", ExampleDate.ToString("MM/dd/yyyy hh:mm tt")),
            new("MMMM dd", ExampleDate.ToString("MMMM dd")),
            new("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK", ExampleDate.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK")),
            new("ddd, dd MMM yyy HH':'mm':'ss 'GMT'", ExampleDate.ToString("ddd, dd MMM yyy HH':'mm':'ss 'GMT'")),
            new("yyyy'-'MM'-'dd'T'HH':'mm':'ss", ExampleDate.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss")),
            new("HH:mm", ExampleDate.ToString("HH:mm")),
            new("hh:mm tt", ExampleDate.ToString("hh:mm tt")),
            new("H:mm", ExampleDate.ToString("H:mm")),
            new("h:mm tt", ExampleDate.ToString("h:mm tt")),
            new("HH:mm:ss", ExampleDate.ToString("HH:mm:ss")),
            new("yyyy MMMM", ExampleDate.ToString("yyyy MMMM")),
        ];
    }
}
