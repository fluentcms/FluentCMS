namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [
            new(nameof(NumberFieldFormInput), "Number Input"),
            new(nameof(NumberFieldFormRange), "Range Input"),
        ];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(NumberFieldDataTableView), "Number")];
    }
}