using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

internal sealed class FieldBuilder(FieldDraft draft) : IFieldBuilder
{
    public IFieldBuilder Required(bool required = true)
    {
        draft.Required = required;
        return this;
    }

    public IFieldBuilder Unique(bool unique = true)
    {
        draft.Unique = unique;
        return this;
    }

    public IFieldBuilder Indexed(bool indexed = true)
    {
        draft.Indexed = indexed;
        return this;
    }

    public IFieldBuilder Searchable(bool searchable = true)
    {
        draft.Searchable = searchable;
        return this;
    }

    public IFieldBuilder IsReadOnly(bool isReadOnly = true)
    {
        draft.ReadOnly = isReadOnly;
        return this;
    }

    public IFieldBuilder Hidden(bool hidden = true)
    {
        draft.Hidden = hidden;
        return this;
    }

    public IFieldBuilder Localizable(bool localizable = true)
    {
        draft.Localizable = localizable;
        return this;
    }

    public IFieldBuilder Multiple(bool multiple = true)
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

    public IFieldBuilder Display(string? label = null, string? help = null, string? placeholder = null, string? group = null, string? tab = null, int width = 0)
    {
        draft.Display = label is null && help is null && placeholder is null && group is null && tab is null && width == 0
            ? null
            : new FieldDisplay { Label = label, Help = help, Placeholder = placeholder, Group = group, Tab = tab, Width = width };
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

    public IFieldBuilder Relation(string target, string? reverse = null, bool ordered = false)
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
