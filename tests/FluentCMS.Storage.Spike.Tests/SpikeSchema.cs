using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluentCMS.Storage.Spike.Tests;

// A stand-in for SchemaModel: everything the EF model needs is plain data known only at runtime.
public sealed record SpikeField(
    string Column,
    Type ClrType,
    bool Localized = false,
    bool Required = false,
    bool Json = false,
    bool Unique = false,
    int? MaxLength = null,
    ValueConverter? Converter = null);

public sealed record SpikeContentType(string Name, IReadOnlyList<SpikeField> Fields, bool Global = false)
{
    public string Table => $"cms_{Name}";

    public string LocaleTable => $"cms_{Name}_i18n";
}

public sealed record SpikeSchema(IReadOnlyList<SpikeContentType> Types, bool UtcTicksDates = false)
{
    public string Key => $"{UtcTicksDates}|" + string.Join(';', Types.Select(t => $"{t.Name}:{t.Global}:{string.Join(',', t.Fields.Select(f => f.Column))}"));

    public static SpikeSchema Article { get; } = new(
    [
        new SpikeContentType("article",
        [
            new SpikeField("title", typeof(string), Localized: true, Required: true, MaxLength: 200),
            new SpikeField("slug", typeof(string), Localized: true, Required: true, Unique: true, MaxLength: 200),
            new SpikeField("body", typeof(string), Localized: true, Json: true),
            new SpikeField("category", typeof(string), Required: true, MaxLength: 64),
            new SpikeField("sections", typeof(string), Json: true),
            new SpikeField("metadata", typeof(string), Json: true),
            new SpikeField("published_on", typeof(DateTimeOffset?), Converter: new UtcTicksConverter()),
            new SpikeField("rating", typeof(decimal?)),
        ]),
    ]);

    public SpikeSchema WithField(string typeName, SpikeField field) =>
        this with { Types = Types.Select(t => t.Name == typeName ? t with { Fields = [.. t.Fields, field] } : t).ToList() };
}
