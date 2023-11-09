namespace FluentCMS.Entities.Identity;

public class UserToken : Entity
{
    public List<string> Tokens { get; set; } = [];
}