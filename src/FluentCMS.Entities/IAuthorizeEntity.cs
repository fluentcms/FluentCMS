namespace FluentCMS.Entities;

public interface IAuthorizeEntity : IEntity
{
    public Guid SiteId { get; set; }
}
