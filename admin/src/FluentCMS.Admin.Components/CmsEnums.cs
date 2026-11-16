namespace FluentCMS.Admin.Components;

/// <summary>The look of a <see cref="CmsButton"/>.</summary>
public enum CmsButtonAppearance
{
    /// <summary>A neutral button.</summary>
    Default,

    /// <summary>The main action of a view, drawn in the accent color.</summary>
    Primary,

    /// <summary>A button with an outline and no fill.</summary>
    Outline,

    /// <summary>A button with no fill and no outline until hovered.</summary>
    Subtle,
}

/// <summary>The color scheme of the admin.</summary>
public enum CmsThemeMode
{
    /// <summary>Follows <c>prefers-color-scheme</c>.</summary>
    System,

    /// <summary>The light theme.</summary>
    Light,

    /// <summary>The dark theme.</summary>
    Dark,
}

/// <summary>The edge of the viewport a <see cref="CmsDrawer"/> opens from, in the reading direction.</summary>
public enum CmsDrawerSide
{
    /// <summary>The end edge: the right in a left-to-right culture.</summary>
    End,

    /// <summary>The start edge: the left in a left-to-right culture.</summary>
    Start,
}

/// <summary>How many rows of a <see cref="CmsDataGrid{TItem}"/> can be selected.</summary>
public enum CmsSelectionMode
{
    /// <summary>No selection.</summary>
    None,

    /// <summary>One row at a time.</summary>
    One,

    /// <summary>Any number of rows.</summary>
    Many,
}

/// <summary>The kind of a toast, which sets its icon and color.</summary>
public enum CmsToastIntent
{
    /// <summary>Information.</summary>
    Info,

    /// <summary>A completed action.</summary>
    Success,

    /// <summary>Something that needs attention.</summary>
    Warning,

    /// <summary>A failure.</summary>
    Error,
}

/// <summary>The kind of text a <see cref="CmsTextInput"/> takes.</summary>
public enum CmsTextInputType
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>An email address.</summary>
    Email,

    /// <summary>A password, masked.</summary>
    Password,

    /// <summary>A URL.</summary>
    Url,

    /// <summary>A search term.</summary>
    Search,

    /// <summary>A telephone number.</summary>
    Telephone,
}

/// <summary>The direction a <see cref="CmsStack"/> lays out its children.</summary>
public enum CmsStackOrientation
{
    /// <summary>One below the other.</summary>
    Vertical,

    /// <summary>Side by side, in the reading direction.</summary>
    Horizontal,
}

/// <summary>The space between the children of a <see cref="CmsStack"/>, from the spacing tokens.</summary>
public enum CmsSpace
{
    /// <summary>No space.</summary>
    None,

    /// <summary><c>--cms-space-s</c>.</summary>
    Small,

    /// <summary><c>--cms-space-m</c>.</summary>
    Medium,

    /// <summary><c>--cms-space-l</c>.</summary>
    Large,
}

/// <summary>The icons of the wrapped set.</summary>
public enum CmsIconName
{
    /// <summary>Add.</summary>
    Add,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>Edit.</summary>
    Edit,

    /// <summary>Save.</summary>
    Save,

    /// <summary>Search.</summary>
    Search,

    /// <summary>Settings.</summary>
    Settings,

    /// <summary>Close.</summary>
    Dismiss,

    /// <summary>More actions.</summary>
    More,

    /// <summary>Upload.</summary>
    Upload,

    /// <summary>A folder.</summary>
    Folder,

    /// <summary>A document.</summary>
    Document,

    /// <summary>An image.</summary>
    Image,

    /// <summary>A person.</summary>
    Person,

    /// <summary>A locale or language.</summary>
    Globe,

    /// <summary>Information.</summary>
    Info,

    /// <summary>A warning.</summary>
    Warning,

    /// <summary>Done or valid.</summary>
    Checkmark,

    /// <summary>Open in a new view.</summary>
    Open,

    /// <summary>Light theme.</summary>
    Sun,

    /// <summary>Dark theme.</summary>
    Moon,
}
