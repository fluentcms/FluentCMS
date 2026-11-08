using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// The neutral default: it leaves the model as ContentModelBuilder made it. A JSON column stays plain
// string text, and an indexed JSON field gets no index, because a B-tree over JSON text is of no use.
public class StorageConventions : IStorageConventions
{
    public virtual string CacheKey => GetType().FullName!;

    public virtual void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    public virtual void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
    }

    public virtual void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
    }

    public virtual void ConfigureJsonIndex(EntityTypeBuilder entity, string column)
    {
    }

    // A v7 value sorts by time as text (SQLite) and as bytes (PostgreSQL).
    public virtual void ConfigureKey(PropertyBuilder key) => key.HasValueGenerator<GuidV7ValueGenerator>();
}
