namespace FluentCMS.Schema;

/// <summary>
/// Requires the value of a text field to match a regular expression. The pattern may match anywhere in the value,
/// as an OpenAPI <c>pattern</c> does, so anchor it with <c>^</c> and <c>$</c> to match the whole value.
/// </summary>
/// <param name="pattern">The regular expression.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RegularExpressionAttribute(string pattern) : Attribute
{
    /// <summary>The regular expression.</summary>
    public string Pattern { get; } = pattern;
}
