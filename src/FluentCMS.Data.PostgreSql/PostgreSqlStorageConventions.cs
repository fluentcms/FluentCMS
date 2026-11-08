using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Data.PostgreSql;

public sealed class PostgreSqlStorageConventions : StorageConventions
{
    public override bool ReordersJson => true;

    // Npgsql writes a DateTimeOffset to timestamptz only with offset 0.
    public override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

    public override void ConfigureJsonColumn(EntityTypeBuilder entity, PropertyBuilder column) =>
        column.HasColumnType("jsonb");

    public override void ConfigureJsonIndex(EntityTypeBuilder entity, string column) =>
        entity.HasIndex(column).HasMethod("gin").HasDatabaseName($"gin_{entity.Metadata.GetTableName()}_{column}");

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value);
}
