using System.Collections.ObjectModel;
using System.Text;

namespace FluentCMS.Schema;

/// <summary>
/// A condition on the values of the sibling fields of an entry, used by <see cref="VisibleWhenAttribute"/>
/// and <see cref="RequiredWhenAttribute"/>. It is data, not code, so it can be serialised into the schema
/// and evaluated by the admin and by agents. Values are stored as strings, booleans, whole numbers, decimals
/// or <see langword="null"/>; an enum member is stored as its camelCase name, as a select field stores it.
/// </summary>
public sealed class Condition
{
    internal Condition(ConditionOperator @operator, string? fieldName, IEnumerable<object?> values, IEnumerable<Condition> operands)
    {
        Operator = @operator;
        FieldName = fieldName;
        Values = new ReadOnlyCollection<object?>([.. values]);
        Operands = new ReadOnlyCollection<Condition>([.. operands]);
    }

    /// <summary>The operation this condition performs.</summary>
    public ConditionOperator Operator { get; }

    /// <summary>The name of the field that is tested, for <c>Equal</c>, <c>In</c> and <c>IsEmpty</c>; otherwise <see langword="null"/>.</summary>
    public string? FieldName { get; }

    /// <summary>The values the field is compared with: one for <c>Equal</c>, any number for <c>In</c>, none otherwise.</summary>
    public IReadOnlyList<object?> Values { get; }

    /// <summary>The conditions that are combined, for <c>And</c>, <c>Or</c> and <c>Not</c>; otherwise empty.</summary>
    public IReadOnlyList<Condition> Operands { get; }

    /// <summary>Starts a condition on a field of the same entry.</summary>
    /// <param name="name">The field name. A property name such as <c>nameof(Category)</c> is converted to the camelCase name the field has in the API.</param>
    /// <returns>A builder whose methods complete the condition.</returns>
    public static ConditionField Field(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new ConditionField(SchemaNaming.ToCamelCase(name));
    }

    /// <summary>Combines this condition with another; both must hold.</summary>
    /// <param name="other">The other condition.</param>
    /// <returns>The combined condition.</returns>
    public Condition And(Condition other) => Combine(ConditionOperator.And, [this, other]);

    /// <summary>Combines this condition with another; at least one must hold.</summary>
    /// <param name="other">The other condition.</param>
    /// <returns>The combined condition.</returns>
    public Condition Or(Condition other) => Combine(ConditionOperator.Or, [this, other]);

    /// <summary>Negates this condition.</summary>
    /// <returns>The negated condition.</returns>
    public Condition Not() => new(ConditionOperator.Not, null, [], [this]);

    /// <inheritdoc/>
    public override string ToString()
    {
        var text = new StringBuilder();
        Append(text);
        return text.ToString();
    }

    // An operand of the same kind is spliced in, so a.And(b).And(c) and And(a, And(b, c)) are the same condition.
    internal static Condition Combine(ConditionOperator @operator, IEnumerable<Condition> operands)
    {
        ArgumentNullException.ThrowIfNull(operands);

        List<Condition> flat = [];
        foreach (var operand in operands)
        {
            ArgumentNullException.ThrowIfNull(operand);
            if (operand.Operator == @operator)
            {
                flat.AddRange(operand.Operands);
            }
            else
            {
                flat.Add(operand);
            }
        }

        if (flat.Count == 0)
        {
            throw new ArgumentException("A condition needs at least one operand.", nameof(operands));
        }

        return flat.Count == 1 ? flat[0] : new Condition(@operator, null, [], flat);
    }

    private void Append(StringBuilder text)
    {
        switch (Operator)
        {
            case ConditionOperator.Equal:
                text.Append(FieldName).Append(" == ").Append(Format(Values[0]));
                break;
            case ConditionOperator.In:
                text.Append(FieldName).Append(" in [").AppendJoin(", ", Values.Select(Format)).Append(']');
                break;
            case ConditionOperator.IsEmpty:
                text.Append(FieldName).Append(" is empty");
                break;
            case ConditionOperator.Not:
                text.Append("not (");
                Operands[0].Append(text);
                text.Append(')');
                break;
            default:
                var separator = Operator == ConditionOperator.And ? " and " : " or ";
                for (var i = 0; i < Operands.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(separator);
                    }

                    text.Append('(');
                    Operands[i].Append(text);
                    text.Append(')');
                }

                break;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
    };
}
