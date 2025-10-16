using FluentCMS.Api.Providers.Email.Abstractions;

namespace FluentCMS.Api.Providers.Email.Null;

public class NullEmailProvider : IEmailProvider
{
    public Task Send(string recipient, string subject, string body, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
