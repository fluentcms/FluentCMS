namespace FluentCMS.Schema;

/// <summary>Shows the field only while a condition holds.</summary>
/// <param name="conditionName">
/// The name of a static property or field of type <see cref="Condition"/> on the class, for example <c>nameof(IsVideo)</c>.
/// </param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VisibleWhenAttribute(string conditionName) : Attribute
{
    /// <summary>The name of the member that holds the condition.</summary>
    public string ConditionName { get; } = conditionName;
}
