namespace FluentCMS.Repositories.EFCore.DbModels;

public class Policy
{
    public string Area { get; set; } = default!;
    public List<string> Actions { get; set; } = [];
}
