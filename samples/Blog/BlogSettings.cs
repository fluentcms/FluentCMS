using System.ComponentModel.DataAnnotations;
using FluentCMS.Settings;

namespace FluentCMS.Samples.Blog;

[Settings("blog")]
public sealed class BlogSettings
{
    [Required, StringLength(100)]
    public string SiteName { get; set; } = "My blog";

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(1, 100)]
    public int ArticlesPerPage { get; set; } = 10;
}
