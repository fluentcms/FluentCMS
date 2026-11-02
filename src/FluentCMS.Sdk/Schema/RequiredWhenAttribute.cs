namespace FluentCMS.Schema;

/// <summary>Requires a value to publish while a condition holds.</summary>
/// <param name="conditionName">
/// The name of a static property or field of type <see cref="Condition"/> on the class, for example <c>nameof(IsVideo)</c>.
/// </param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RequiredWhenAttribute(string conditionName) : Attribute
{
    /// <summary>The name of the member that holds the condition.</summary>
    public string ConditionName { get; } = conditionName;
}
