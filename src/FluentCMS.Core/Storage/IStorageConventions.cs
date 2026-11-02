using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// What a data provider package adds to the provider-neutral model. The model cache key includes the
// implementing type, so an implementation must always produce the same changes.
public interface IStorageConventions
{
    // Model-wide conventions, such as storing DateTimeOffset as UTC ticks on SQLite.
    void ConfigureConventions(ModelConfigurationBuilder configurationBuilder);

    // Every JSON text column, system ones included: its column type (jsonb, nvarchar(max)) and any check
    // such as json_valid. The check goes on the table through entity.ToTable(table => ...).
    void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column);

    // A JSON column of an [Indexed] field, such as a GIN index on PostgreSQL.
    void ConfigureJsonIndex(EntityTypeBuilder entity, string column);
}
