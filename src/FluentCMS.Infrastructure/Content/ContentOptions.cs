namespace FluentCMS.Infrastructure.Content;

public sealed class ContentOptions
{
    public const string SectionName = "Content";

    // How long a deleted entry can be restored (CNT-06, REL-03).
    public TimeSpan DeletedRetention { get; set; } = TimeSpan.FromDays(30);
}
