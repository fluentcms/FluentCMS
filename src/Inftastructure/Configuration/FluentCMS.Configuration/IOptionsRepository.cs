namespace FluentCMS.Configuration;

public interface IOptionsRepository
{
    Task EnsureCreated(CancellationToken cancellationToken = default);
    Task<int> Upsert(OptionRegistration registration, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string?>> GetAllSections(CancellationToken cancellationToken = default);
}