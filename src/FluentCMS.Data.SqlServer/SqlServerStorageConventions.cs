using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FluentCMS.Data.SqlServer;

// JSON is nvarchar(max) text with an ISJSON check on every version, including 2025, whose native json type the
// system migrations do not create (ADR 0001).
public sealed class SqlServerStorageConventions : StorageConventions
{
    // The other providers compare text case-sensitively, and a database's default collation usually does not.
    internal const string BinaryCollation = "Latin1_General_100_BIN2";

    // datetimeoffset keeps the offset it is given, so a date would read back in the writer's offset.
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column)
    {
        column.HasColumnType("nvarchar(max)");

        var name = column.Metadata.GetColumnName();
        entity.ToTable(table => table.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{name}_json", $"ISJSON([{name}]) = 1"));
    }

    public override void ConfigureTextColumn(EntityTypeBuilder entity, PropertyBuilder column) =>
        column.UseCollation(BinaryCollation);

    // uniqueidentifier sorts by its last six bytes first, where a v7 value is random, so v7 keys would land all over the
    // clustered index. These rise within a process, which is what the index needs; nothing compares ids for time order.
    public override void ConfigureKey(PropertyBuilder key) => key.HasValueGenerator<SequentialGuidValueGenerator>();

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
}
