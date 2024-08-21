namespace FluentCMS.Providers;

public interface IApiTokenProvider
{
    public string GenerateKey();
    public string GenerateSecret(string apiKey);
    public bool Valiadate(string apiKey, string secretKey);
}
