namespace FluentCMS.Web.UI.Plugins.General.HelloWorld;

public class HelloWorldContent
{
    public string Content { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Type { get; set; } = "HelloWorldContent";
}
