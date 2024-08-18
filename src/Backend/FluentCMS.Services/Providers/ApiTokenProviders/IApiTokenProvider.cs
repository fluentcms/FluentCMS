namespace FluentCMS.Providers;

public interface IApiTokenProvider
{
    public string GenerateKey();
    public string GenerateSecret(string apiKey);
}
