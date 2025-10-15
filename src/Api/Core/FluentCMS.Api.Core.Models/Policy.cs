namespace FluentCMS.Api.Core.Models;

public class Policy : Entity
{
    public string Area { get; set; } = default!;
    public List<string> Actions { get; set; } = [];
    public Guid ApiTokenId { get; set; }
}
