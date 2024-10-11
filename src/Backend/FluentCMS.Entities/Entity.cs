namespace FluentCMS.Entities;

public interface IEntity
{
    /// <summary>
    /// RavenDB cannot use Guid as Id and will automatically try to set an internal id for all documents.
    /// To override that we need another property to store the internal RavenId.
    /// This Id is not used elsewhere in the program.
    /// </summary>
    string RavenId { get; set; }

    Guid Id { get; set; }
}

public abstract class Entity : IEntity
{
    public string RavenId { get; set; }
    public Guid Id { get; set; }
}
