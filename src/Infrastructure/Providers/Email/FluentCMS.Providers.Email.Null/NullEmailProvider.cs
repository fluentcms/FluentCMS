using FluentCMS.Providers.Email.Abstractions;

namespace FluentCMS.Providers.Email.Null;

public class NullEmailProvider : IEmailProvider
{
    public Task Send(string recipient, string subject, string body, IDictionary<string, string>? headers = null)
    {
        return Task.CompletedTask;
    }
}