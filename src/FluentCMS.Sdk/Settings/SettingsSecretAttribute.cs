namespace FluentCMS.Settings;

/// <summary>
/// Marks a property of a settings class as a secret. Its value is never put in an event, an activity row or a log line,
/// and the JSON schema of the class shows no default for it and marks it write-only. A property whose name suggests a
/// secret is treated the same way without this attribute: one whose last word is <c>password</c>, <c>passphrase</c>, <c>secret</c> or <c>token</c>,
/// or whose last two words are <c>api key</c>, <c>private key</c>, <c>secret key</c>, <c>access key</c>, <c>client secret</c> or <c>connection string</c>, such as <c>SmtpPassword</c> or <c>ApiKey</c>.
/// Only a property of the settings class itself can be secret; one below it that is marked or named so stops the host.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SettingsSecretAttribute : Attribute;
