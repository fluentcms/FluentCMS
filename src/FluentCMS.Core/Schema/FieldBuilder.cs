using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class FieldBuilder(FieldDraft draft) : IFieldBuilder
{
    public IFieldBuilder Required(bool required)
    {
        draft.Required = required;
        return this;
    }

    public IFieldBuilder Unique(bool unique)
    {
        draft.Unique = unique;
        return this;
    }

    public IFieldBuilder Indexed(bool indexed)
    {
        draft.Indexed = indexed;
        return this;
    }

    public IFieldBuilder Searchable(bool searchable)
    {
        draft.Searchable = searchable;
        return this;
    }

    public IFieldBuilder IsReadOnly(bool isReadOnly)
    {
        draft.ReadOnly = isReadOnly;
        return this;
    }

    public IFieldBuilder Hidden(bool hidden)
    {
        draft.Hidden = hidden;
        return this;
    }

    public IFieldBuilder Localizable(bool localizable)
    {
        draft.Localizable = localizable;
        return this;
    }

    public IFieldBuilder Multiple(bool multiple)
    {
        draft.Multiple = multiple;
        return this;
    }

    public IFieldBuilder Length(int? min, int? max)
    {
        draft.MinLength = min;
        draft.MaxLength = max;
        return this;
    }

    public IFieldBuilder Range(decimal? min, decimal? max)
    {
        draft.Minimum = min is null ? null : SchemaValue.NormalizeDecimal(min.Value);
        draft.Maximum = max is null ? null : SchemaValue.NormalizeDecimal(max.Value);
        return this;
    }

    public IFieldBuilder Pattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        draft.Pattern = pattern;
        return this;
    }

    public IFieldBuilder Display(FieldDisplayDescription display)
    {
        ArgumentNullException.ThrowIfNull(display);

        draft.Display = display == new FieldDisplayDescription()
            ? null
            : new FieldDisplay { Label = display.Label, Help = display.Help, Placeholder = display.Placeholder, Group = display.Group, Tab = display.Tab, Width = display.Width };
        return this;
    }

    public IFieldBuilder DefaultValue(object? value)
    {
        draft.DefaultValue = SchemaValue.Normalize(value, allowList: true);
        return this;
    }

    public IFieldBuilder FormerNames(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        draft.FormerNames = [.. names];
        return this;
    }

    public IFieldBuilder VisibleWhen(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        draft.VisibleWhen = condition;
        return this;
    }

    public IFieldBuilder RequiredWhen(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        draft.RequiredWhen = condition;
        return this;
    }

    public IFieldBuilder Relation(string target, string? reverse, bool ordered)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        draft.Relation = new FieldRelation { Target = target, Reverse = reverse, Ordered = ordered };
        return this;
    }

    public IFieldBuilder Setting(string name, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        draft.Settings[name] = SchemaValue.Normalize(value, allowList: true)!;
        return this;
    }
}
