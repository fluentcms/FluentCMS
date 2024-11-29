namespace FluentCMS.Repositories.EFCore.DbModels;

public interface IEntityModel
{
    Guid Id { get; set; }
}

public abstract class EntityModel : IEntityModel
{
    public Guid Id { get; set; }
}
