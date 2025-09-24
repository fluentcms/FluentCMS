using FluentCMS.Providers.Abstractions;

namespace FluentCMS.Providers.Email.Abstractions;

public interface IEmailProvider : IProvider
{
    public const string Area = "Email";
    Task Send(string recipient, string subject, string body, IDictionary<string, string>? headers = null);
}