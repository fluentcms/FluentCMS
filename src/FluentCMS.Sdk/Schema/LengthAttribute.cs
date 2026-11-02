namespace FluentCMS.Schema;

/// <summary>Limits the number of characters of a text field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LengthAttribute : Attribute
{
    /// <summary>The least number of characters. 0 sets no limit.</summary>
    public int Min { get; set; }

    /// <summary>The most number of characters. <see cref="int.MaxValue"/> sets no limit.</summary>
    public int Max { get; set; } = int.MaxValue;
}
