namespace FluentCMS.Settings;

/// <summary>
/// Marks a property of a settings class as a secret. Its value is never put in an event, an activity row or a log line,
/// and the JSON schema of the class shows no default for it and marks it write-only. A property whose name suggests a
/// secret, such as one that contains <c>token</c>, <c>secret</c>, <c>password</c> or <c>key</c>, is treated the same way without this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SettingsSecretAttribute : Attribute;
