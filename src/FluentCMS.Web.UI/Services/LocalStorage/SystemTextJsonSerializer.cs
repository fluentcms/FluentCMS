using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FluentCMS.Web.UI.Services.LocalStorage;

public interface IJsonSerializer
{
    string Serialize<T>(T obj);
    T Deserialize<T>(string text);
}

internal class SystemTextJsonSerializer : IJsonSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonSerializer(IOptions<LocalStorageOptions> options)
    {
        _options = options.Value.JsonSerializerOptions;
    }

    public SystemTextJsonSerializer(LocalStorageOptions localStorageOptions)
    {
        _options = localStorageOptions.JsonSerializerOptions;
    }

    public T Deserialize<T>(string data)
        => JsonSerializer.Deserialize<T>(data, _options);

    public string Serialize<T>(T data)
        => JsonSerializer.Serialize(data, _options);
}

