namespace FluentCMS.Infrastructure.Checks;

// One version of a check definition; inserted once and never changed, so the version a result names stays readable.
public sealed class CheckVersionRow
{
    public Guid Id { get; set; }

    public Guid CheckId { get; set; }

    public int Version { get; set; }

    public string Definition { get; set; } = string.Empty;
}
