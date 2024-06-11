namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class StringFieldModel : FieldModel<string?>
{
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public decimal? MinimumLength { get; set; }
    public decimal? MaximumLength { get; set; }
    public bool Unique { get; set; }
    public string? Pattern { get; set; }

    #region Overrides

    public override string Type { get; set; } = "string";

    public override Type GetFieldSettingComponentType()
    {
        return typeof(StringFieldSettings);
    }

    public override List<ComponentTypeOption> GetDataTableComponents()
    {
        return [new(typeof(StringFieldDataTableView), "Simple Text")];
    }

    public override List<ComponentTypeOption> GetFormComponents()
    {
        return
        [
            new(typeof(StringFieldFormText), "Input"),
            new(typeof(StringFieldFormTextArea), "Text Area"),
            new(typeof(StringFieldFormMarkdownText), "Markdown"),
            new(typeof(StringFieldFormRichText), "Rich Text"),
        ];
    }

    #endregion

}
