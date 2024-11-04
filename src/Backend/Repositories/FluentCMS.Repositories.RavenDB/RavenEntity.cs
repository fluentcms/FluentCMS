using System;

namespace FluentCMS.Repositories.RavenDB;

public class RavenEntity<TEntity> where TEntity : IEntity
{
    public RavenEntity(TEntity entity)
    {
        Data = entity;
    }

    public string RavenId { get; set; } = default!;

    public TEntity Data { get; set; }
}
