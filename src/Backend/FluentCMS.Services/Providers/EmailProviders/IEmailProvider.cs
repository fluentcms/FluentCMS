namespace FluentCMS.Providers;
public interface IEmailProvider
{
    Task Send(string from, string recipients, string? subject, string? body, CancellationToken cancellationToken = default);
    Task Send(string recipients, string? subject, string? body, CancellationToken cancellationToken = default);
}
