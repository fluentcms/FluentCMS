namespace FluentCMS.Entities;

public class UserToken : Entity
{
    public List<string> Tokens { get; set; } = [];
}