namespace FluentCMS.Web.UI;

public class SiteContext
{
    public SiteDetailResponse Site { get; set; } = default!;
    public IEnumerable<PageDetailResponse> Pages { get; set; } = default!;
}
