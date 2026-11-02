namespace FluentCMS.Schema;

/// <summary>Sets the options of one field of a type defined without a class.</summary>
public interface IFieldBuilder
{
    /// <summary>Sets whether a value is required to publish.</summary>
    /// <param name="required">Whether the field is required.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Required(bool required = true);

    /// <summary>Sets whether the value must be unique among published entries.</summary>
    /// <param name="unique">Whether the field is unique.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Unique(bool unique = true);

    /// <summary>Sets whether the field has a database index.</summary>
    /// <param name="indexed">Whether the field is indexed.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Indexed(bool indexed = true);

    /// <summary>Sets whether the field is included in full-text search.</summary>
    /// <param name="searchable">Whether the field is searchable.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Searchable(bool searchable = true);

    /// <summary>Sets whether clients cannot change the value.</summary>
    /// <param name="isReadOnly">Whether the field is read-only.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder IsReadOnly(bool isReadOnly = true);

    /// <summary>Sets whether the field is left out of responses and forms.</summary>
    /// <param name="hidden">Whether the field is hidden.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Hidden(bool hidden = true);

    /// <summary>Sets whether the value differs per locale. The type must be localized.</summary>
    /// <param name="localizable">Whether the field is localizable.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Localizable(bool localizable = true);

    /// <summary>Sets whether the field holds a list, such as a multi select, a to-many relation or a repeatable component.</summary>
    /// <param name="multiple">Whether the field holds a list.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Multiple(bool multiple = true);

    /// <summary>Limits the number of characters.</summary>
    /// <param name="min">The least number, or <see langword="null"/> for no limit.</param>
    /// <param name="max">The most number, or <see langword="null"/> for no limit.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Length(int? min, int? max);

    /// <summary>Limits the value of a number.</summary>
    /// <param name="min">The least value, or <see langword="null"/> for no limit.</param>
    /// <param name="max">The greatest value, or <see langword="null"/> for no limit.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Range(decimal? min, decimal? max);

    /// <summary>Requires the value to match a regular expression.</summary>
    /// <param name="pattern">The regular expression.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Pattern(string pattern);

    /// <summary>Sets the hints that tell the admin how to present the field.</summary>
    /// <param name="label">The label.</param>
    /// <param name="help">The help text.</param>
    /// <param name="placeholder">The placeholder.</param>
    /// <param name="group">The group of fields the field is shown with.</param>
    /// <param name="tab">The tab the field is shown on.</param>
    /// <param name="width">The width in columns of a twelve-column grid, or 0 to leave it to the admin.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Display(string? label = null, string? help = null, string? placeholder = null, string? group = null, string? tab = null, int width = 0);

    /// <summary>Sets the value a new entry starts with.</summary>
    /// <param name="value">A string, boolean, number, enum member, list of strings or <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder DefaultValue(object? value);

    /// <summary>Records the earlier names of the field, so a rename is not taken for a drop and an add.</summary>
    /// <param name="names">The earlier names.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder FormerNames(params string[] names);

    /// <summary>Shows the field only while a condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder VisibleWhen(Condition condition);

    /// <summary>Requires a value while a condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder RequiredWhen(Condition condition);

    /// <summary>Sets the target of a relation field.</summary>
    /// <param name="target">The name of the content type the relation points to.</param>
    /// <param name="reverse">The name of the reverse side on the target type, or <see langword="null"/> for none.</param>
    /// <param name="ordered">Whether the order of the related entries is kept.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Relation(string target, string? reverse = null, bool ordered = false);

    /// <summary>Sets a setting of the field type, keyed as in <see cref="FieldSettings"/> for the built-in types.</summary>
    /// <param name="name">The key.</param>
    /// <param name="value">A string, boolean, number, enum member or list of strings.</param>
    /// <returns>This builder.</returns>
    IFieldBuilder Setting(string name, object value);
}
