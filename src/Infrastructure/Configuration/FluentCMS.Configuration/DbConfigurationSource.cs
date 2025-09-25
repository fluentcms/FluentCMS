using Microsoft.Extensions.Configuration;

namespace FluentCMS.Configuration;

public sealed class DbConfigurationSource : IConfigurationSource
{
    public IOptionsRepository Repository { get; set; } = default!;
    public TimeSpan? ReloadInterval { get; set; }
    public DbConfigurationProvider Provider { get; private set; } = default!;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new DbConfigurationProvider(this);
        return Provider;
    }
}