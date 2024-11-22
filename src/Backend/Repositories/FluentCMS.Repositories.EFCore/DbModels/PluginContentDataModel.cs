namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("PluginContentData")]
public class PluginContentDataModel : EntityModel
{
    public Guid PluginContentId { get; set; } // foreign key
    public string Key { get; set; } = default!; // key for the dictionary
    public string Value { get; set; } = default!; // JSON string
    public PluginContentModel PluginContent { get; set; } = default!; // navigation property
}
