namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [
            new(nameof(StringFieldFormText), "Input"),
            new(nameof(StringFieldFormTextArea), "Textarea"),
            new(nameof(StringFieldFormMarkdownText), "Markdown"),
            new(nameof(StringFieldFormRichText), "Rich Text"),
        ];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(StringFieldDataTableView), "Simple Text")];
    }
}