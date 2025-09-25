namespace FluentCMS.Configuration;

public sealed class ProviderCatalogOptions
{
    // Use HashSet to avoid duplicates 
    public HashSet<OptionRegistration> Types { get; } = [];
}