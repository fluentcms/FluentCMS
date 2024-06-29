namespace FluentCMS.Web.Api.Models;

public class Policy
{
    public string Area { get; set; } = default!;
    public List<string> Actions { get; set; } = [];
}
