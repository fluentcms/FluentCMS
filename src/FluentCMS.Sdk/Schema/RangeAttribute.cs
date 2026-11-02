namespace FluentCMS.Schema;

/// <summary>Limits the value of a number field. Use an infinity for a bound that is not set.</summary>
/// <param name="min">The least value, or <see cref="double.NegativeInfinity"/>.</param>
/// <param name="max">The greatest value, or <see cref="double.PositiveInfinity"/>.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RangeAttribute(double min, double max) : Attribute
{
    /// <summary>The least value.</summary>
    public double Min { get; } = min;

    /// <summary>The greatest value.</summary>
    public double Max { get; } = max;
}
