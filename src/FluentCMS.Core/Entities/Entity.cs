namespace FluentCMS.Core.Entities
{
    public interface IEntity<TKey> where TKey : IEquatable<TKey>
    {
        TKey Id { get; set; }
    }

    public interface IEntity : IEntity<Guid>
    {
    }

    public class Entity<TKey> : IEntity<TKey> where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; } = default!;
    }

    public class Entity : Entity<Guid>, IEntity
    {
    }
}
