namespace FluentCMS.Schema;

/// <summary>A field named in a <see cref="Condition"/>; its methods complete the condition.</summary>
public sealed class ConditionField
{
    internal ConditionField(string name)
    {
        Name = name;
    }

    /// <summary>The name of the field.</summary>
    public string Name { get; }

    /// <summary>Holds when the field equals a value.</summary>
    /// <param name="value">The value: a string, boolean, number, enum member or <see langword="null"/>.</param>
    /// <returns>The condition.</returns>
    public new Condition Equals(object? value) =>
        new(ConditionOperator.Equal, Name, [SchemaValue.Normalize(value)], []);

    /// <summary>Holds when the field equals one of the values.</summary>
    /// <param name="values">The values, as for <see cref="Equals(object?)"/>.</param>
    /// <returns>The condition.</returns>
    public Condition In(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(ConditionOperator.In, Name, values.Select(value => SchemaValue.Normalize(value)), []);
    }

    /// <summary>Holds when the field has no value: <see langword="null"/>, an empty or whitespace-only string, or an empty list.</summary>
    /// <returns>The condition.</returns>
    public Condition IsEmpty() => new(ConditionOperator.IsEmpty, Name, [], []);
}
