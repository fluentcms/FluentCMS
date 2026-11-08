using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Core.Storage;

// The neutral default: it leaves the model as ContentModelBuilder made it. A JSON column stays plain
// string text, and an indexed JSON field gets no index, because a B-tree over JSON text is of no use.
public class StorageConventions : IStorageConventions
{
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Microseconds = new(
        value => ToMicroseconds(value),
        value => value);

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

    // What every context applies: the provider's conventions, then the ones every provider shares.
    internal static void Configure(IStorageConventions conventions, ModelConfigurationBuilder configurationBuilder)
    {
        conventions.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new MicrosecondsConvention());
    }

    // PostgreSQL keeps a DateTimeOffset to the microsecond, so every provider stores it so: a value reads back the same on
    // each, and a parameter is cut as the column it is compared with.
    internal static DateTimeOffset ToMicroseconds(DateTimeOffset value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    // Runs once the provider has set its own converter, which then gets the value already cut.
    private sealed class MicrosecondsConvention : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(type => type.GetDeclaredProperties()))
            {
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(property.GetValueConverter() is { } provider ? Microseconds.ComposeWith(provider) : Microseconds);
                }
            }
        }
    }
}
