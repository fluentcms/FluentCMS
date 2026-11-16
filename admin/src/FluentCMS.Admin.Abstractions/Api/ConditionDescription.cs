using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Api;

/// <summary>A condition on the sibling fields of an entry, which a form evaluates to show or require a field.</summary>
public sealed record ConditionDescription
{
    /// <summary>The operation, such as <c>equal</c>, <c>in</c>, <c>isEmpty</c>, <c>and</c>, <c>or</c> or <c>not</c>.</summary>
    public required string Operator { get; init; }

    /// <summary>The field that is tested, for <c>equal</c>, <c>in</c> and <c>isEmpty</c>.</summary>
    public string? Field { get; init; }

    /// <summary>The values the field is compared with, for <c>equal</c> and <c>in</c>.</summary>
    public JsonArray? Values { get; init; }

    /// <summary>The conditions that are combined, for <c>and</c>, <c>or</c> and <c>not</c>.</summary>
    public IReadOnlyList<ConditionDescription>? Operands { get; init; }
}
