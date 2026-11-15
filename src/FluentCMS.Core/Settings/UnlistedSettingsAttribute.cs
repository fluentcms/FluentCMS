namespace FluentCMS.Core.Settings;

// An area that core keeps for its own state, such as the record of the seeders, which no administrator is meant to edit, so
// the list of areas of the Management API leaves it out. It is internal: every area of a plugin or of the host is listed.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class UnlistedSettingsAttribute : Attribute;
