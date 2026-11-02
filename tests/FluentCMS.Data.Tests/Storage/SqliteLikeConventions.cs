using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Data.Tests.Storage;

// What the SQLite package is expected to do in #2689, to show that the hooks reach the model.
internal sealed class SqliteLikeConventions : StorageConventions
{
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();

    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
        var name = column.Metadata.Name;
        var columnName = column.Metadata.GetColumnName();
        entity.ToTable(table => table.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{name}_json", $"json_valid({columnName})"));
    }

    public override void ConfigureJsonIndex(EntityTypeBuilder entity, string column) =>
        entity.HasIndex(column).HasDatabaseName($"gin_{entity.Metadata.GetTableName()}_{column}");

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));
}
