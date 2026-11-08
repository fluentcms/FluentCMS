using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// What a data provider package adds to the provider-neutral model.
public interface IStorageConventions
{
    // Part of the model cache key: two instances that change the model differently need different keys.
    string CacheKey { get; }

    // Model-wide conventions, such as storing DateTimeOffset as UTC ticks on SQLite.
    void ConfigureConventions(ModelConfigurationBuilder configurationBuilder);

    // Every JSON text column, system ones included: its column type (jsonb, nvarchar(max)) and any check
    // such as json_valid. The check goes on the table through entity.ToTable(table => ...).
    void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column);

    // Every text column of a content field that is not JSON, such as its collation on SQL Server.
    void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column);

    // A JSON column of an [Indexed] field, such as a GIN index on PostgreSQL.
    void ConfigureJsonIndex(EntityTypeBuilder entity, string column);

    // Whether a JSON column gives back other text than was written, as jsonb does with its key order and spacing, so that a
    // read puts each value back in the form a write gives it.
    bool ReordersJson { get; }

    // Every Guid id that EF generates, on system and content tables: its value generator, whose values should rise in
    // the order the provider sorts its uuid type, so that inserts append to the primary key. An id the code sets stays.
    void ConfigureKey(PropertyBuilder key);
}
