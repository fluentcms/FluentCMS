using FluentCMS.Infrastructure.Providers.Abstractions;

namespace FluentCMS.Api.Providers.Email.Abstractions;

public interface IEmailProvider : IProvider
{
    public const string Area = "Email";
    Task Send(string recipient, string subject, string body, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
}
