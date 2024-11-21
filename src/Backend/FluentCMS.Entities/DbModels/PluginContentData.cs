namespace FluentCMS.Repositories.EFCore.DbModels;

public class PluginContentData : Entity
{
    public Guid PluginContentId { get; set; } // foreign key
    public string Key { get; set; } = default!; // key for the dictionary
    public string Value { get; set; } = default!; // JSON string
    public PluginContent PluginContent { get; set; } = default!; // navigation property
}
