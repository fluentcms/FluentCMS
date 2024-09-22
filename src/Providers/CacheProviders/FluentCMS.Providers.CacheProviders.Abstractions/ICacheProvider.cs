namespace FluentCMS.Providers.CacheProviders;

public interface ICacheProvider
{
    void Remove(string key);
    void Set<T>(string key, T value);
    bool TryGetValue<T>(object key, out T? value);
}
