using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class FieldDraft(string name, string fieldType)
{
    public string Name { get; } = name;

    public string FieldType { get; } = fieldType;

    public bool Multiple { get; set; }

    public bool Required { get; set; }

    public bool Unique { get; set; }

    public bool Indexed { get; set; }

    public bool Searchable { get; set; }

    public bool ReadOnly { get; set; }

    public bool Hidden { get; set; }

    public bool Localizable { get; set; }

    public string[] FormerNames { get; set; } = [];

    public FieldDisplay? Display { get; set; }

    public object? DefaultValue { get; set; }

    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    public decimal? Minimum { get; set; }

    public decimal? Maximum { get; set; }

    public string? Pattern { get; set; }

    public Condition? VisibleWhen { get; set; }

    public Condition? RequiredWhen { get; set; }

    public FieldRelation? Relation { get; set; }

    public Dictionary<string, object> Settings { get; } = [];
}
