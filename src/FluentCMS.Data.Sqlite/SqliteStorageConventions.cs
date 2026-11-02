using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Data.Sqlite;

public sealed class SqliteStorageConventions : StorageConventions
{
    // SQLite compares DateTimeOffset only for equality, so every date is UTC ticks, which also drops the offset.
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();

    // One row of malformed JSON makes every JSON filter over the table throw, so it must not get in.
    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
        var name = column.Metadata.GetColumnName();
        entity.ToTable(table => table.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{name}_json", $"json_valid(\"{name}\")"));
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));
}
