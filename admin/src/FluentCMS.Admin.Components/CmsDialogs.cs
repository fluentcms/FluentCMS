namespace FluentCMS.Admin.Components;

// Sealed, and CmsDialogBase cannot be derived from outside the set, so that no derived dialog misses the base's disposal or
// renders against its handling of events.

/// <summary>A modal dialog named by its title, with a close button in its title.</summary>
public sealed partial class CmsDialog;

/// <summary>A modal dialog docked to an edge of the viewport, in the reading direction.</summary>
public sealed partial class CmsDrawer;
