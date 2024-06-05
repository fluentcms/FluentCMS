namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    public const string FORM_NAME = "ContentTypeFieldCreateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldType? FieldType { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    #region Text 

    private string MetaViewType { get; set; }
    private string MetaPattern { get; set; }
    private bool MetaUnique { get; set; }
    private bool MetaMinimumEnabled { get; set; }
    private int MetaMinimum { get; set; }
    private bool MetaMaximumEnabled { get; set; }
    private int MetaMaximum { get; set; }
    private string MetaDefaultValue { get; set; }
 
    class SelectItem {
        public string Key { get; set; }
        public string Text { get; set; }
    };

    private List<SelectItem> TextViewTypes = new List<SelectItem> {
        new()
        {
            Key="input",
            Text="Input"
        },
        new()
        {
            Key="textarea",
            Text="Textarea"
        },
        new()
        {
            Key="markdown",
            Text="Markdown"
        },
        new()
        {
            Key="rich-text",
            Text="Rich Text"
        },
    };

    #endregion

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        Model ??= new();
        Model.Settings ??= new();
    }

    private async Task OnBack()
    {
        Model!.Type = default!;
        FieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        Model!.Type = type.Key;

        // Model!.Settings.Meta.DefaultValue = MetaDefaultValue;
        // Model!.Settings.Meta.Unique = MetaUnique;
        // Model!.Settings.Meta.MinimumEnabled = MetaMinimumEnabled;
        // Model!.Settings.Meta.Minimum = MetaMinimum;
        // Model!.Settings.Meta.MaximumEnabled = MetaMaximumEnabled;
        // Model!.Settings.Meta.Pattern = MetaPattern;
        // Model!.Settings.Meta.ViewType = MetaViewType;
        
        FieldType = type;
        await Task.CompletedTask;
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync();
    }
}
