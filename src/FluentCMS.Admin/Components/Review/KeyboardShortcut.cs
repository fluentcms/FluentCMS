namespace FluentCMS.Admin.Components.Review;

// One line of the shortcuts dialog: the keys as they are printed, and what they do.
public sealed record KeyboardShortcut(IReadOnlyList<string> Keys, string Description);
