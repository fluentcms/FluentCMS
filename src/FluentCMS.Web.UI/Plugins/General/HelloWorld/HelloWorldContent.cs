using FluentCMS.Entities;

namespace FluentCMS.Web.UI.Plugins.General.HelloWorld;

public class HelloWorldContent
{
    public string Content { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }

    public HelloWorldContent()
    {
    }

    public HelloWorldContent(Content content)
    {
        Id = content.Id;
        SiteId = content.SiteId;
        Content = content.ContainsKey("Content") ? content["Content"]?.ToString() ?? string.Empty : string.Empty;
    }

    public PluginContent ToContent()
    {
        var content = new PluginContent
        {
            Id = Id,
            SiteId = SiteId,
            Type = "HelloWorld"
        };
        content.Add("Content", Content);
        return content;
    }
}
