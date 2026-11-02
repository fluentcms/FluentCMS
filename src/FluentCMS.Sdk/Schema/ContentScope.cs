namespace FluentCMS.Schema;

/// <summary>Whether the entries of a content type belong to one site or are shared by all sites.</summary>
public enum ContentScope
{
    /// <summary>Each entry belongs to one site. The default.</summary>
    Site = 0,

    /// <summary>One set of entries is shared by all sites; their site id is <see langword="null"/>.</summary>
    Global = 1,
}
