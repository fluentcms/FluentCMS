﻿namespace FluentCMS.Web.Plugins.Contents.News;

public class NewsContent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
