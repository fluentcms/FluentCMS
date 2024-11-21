namespace FluentCMS.Repositories.EFCore.DbModels;

public class Policy : Entity
{
    public Guid ApiTokenId { get; set; } // foreign key
    public string Area { get; set; } = default!;
    public string Actions { get; set; } = default!;
    public ApiToken ApiToken { get; set; } = default!; // navigation property
}
