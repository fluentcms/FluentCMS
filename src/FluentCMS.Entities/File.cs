﻿namespace FluentCMS.Entities;

public class File : AuditableEntity
{
    /// <summary>
    /// The original name of the file within the physical file system prior to upload.
    /// The name of the file when downloaded should match this property, e.g., 'Untitled.png', 'Default.png', etc.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The file extension, stripped of the file name, to facilitate filtering by extension, e.g., '.png'.
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// The MIME type of the file, such as 'image/png'.
    /// I'm Uncertain but may prove useful for content delivery networks (CDNs) to filter and locate files.
    /// Consideration may be given to storing each file type with a different provider.
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; } = 0;
}
