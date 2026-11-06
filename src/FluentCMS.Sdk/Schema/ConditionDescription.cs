namespace FluentCMS.Schema;

/// <summary>
/// A <see cref="Condition"/> as data a client can evaluate against the sibling fields of an entry. Values are strings,
/// booleans, numbers or <see langword="null"/>; an enum member is its camelCase name, as a select field stores it.
/// </summary>
public sealed record ConditionDescription
{
    /// <summary>The operation the condition performs.</summary>
    public required ConditionOperator Operator { get; init; }

    /// <summary>The name of the field that is tested, for <c>Equal</c>, <c>In</c> and <c>IsEmpty</c>; otherwise <see langword="null"/>.</summary>
    public string? Field { get; init; }

    /// <summary>The values the field is compared with, for <c>Equal</c> (one) and <c>In</c>; otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<object?>? Values { get; init; }

    /// <summary>The conditions that are combined, for <c>And</c>, <c>Or</c> and <c>Not</c> (one); otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<ConditionDescription>? Operands { get; init; }
}
