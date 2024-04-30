namespace FluentCMS.Entities;
public class Policy
{
    public string Area { get; set; } = default!;
    public List<string> Actions { get; set; } = [];
}
