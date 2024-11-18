namespace FluentCMS.Repositories.EFCore.DbModels;

public class ApiTokenPolicy
{
    public Guid Id { get; set; }
    public Guid ApiTokenId { get; set; }
    public string Area { get; set; } = default!;
    public string Actions { get; set; } = string.Empty;
    public ApiToken ApiToken { get; set; } = default!;
}
