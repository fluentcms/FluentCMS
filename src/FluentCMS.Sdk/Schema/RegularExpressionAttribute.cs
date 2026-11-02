namespace FluentCMS.Schema;

/// <summary>Requires the value of a text field to match a regular expression.</summary>
/// <param name="pattern">The regular expression.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RegularExpressionAttribute(string pattern) : Attribute
{
    /// <summary>The regular expression.</summary>
    public string Pattern { get; } = pattern;
}
