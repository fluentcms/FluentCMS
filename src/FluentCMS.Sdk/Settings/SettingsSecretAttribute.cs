namespace FluentCMS.Settings;

/// <summary>
/// Marks a property of a settings class as a secret. Its value is never put in an event, an activity row or a log line,
/// and the JSON schema of the class shows no default for it and marks it write-only. A property whose name suggests a
/// secret is treated the same way without this attribute, as a safety net for a forgotten one (this attribute is the real control): one whose last word is <c>password</c>, <c>passphrase</c>, <c>secret</c>, <c>token</c>, <c>pwd</c> or <c>apikey</c>,
/// or whose last two words are <c>api</c>, <c>private</c>, <c>secret</c>, <c>access</c>, <c>signing</c> or <c>encryption</c> followed by <c>key</c>, <c>client secret</c> or <c>connection string</c>, in the singular or the plural, such as <c>SmtpPassword</c> or <c>ApiKey</c>.
/// Only a property of the settings class itself can be secret; one below it that is marked or named so stops the host.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SettingsSecretAttribute : Attribute;
