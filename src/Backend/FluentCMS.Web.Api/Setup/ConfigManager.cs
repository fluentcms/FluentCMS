using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace FluentCMS.Web.Api;

public class ApiServerConfig
{
    public DatabaseConfig Database { get; set; } = default!;
    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = default!;
        public string Provider { get; set; } = default!;
    }
}

public interface IConfigManager
{
    public ApiServerConfig GetApiServerConfig();
    public void UpdateApiServerConfig(ApiServerConfig apiServerConfig);
    public bool IsConfigured();
}

public class ConfigManager : IConfigManager
{
    private const string _settingFileName = "appsettings.json";
    private const string _apiServerConfigKey = "ApiServerConfig";
    private readonly IConfigurationRoot _configRoot;
    public ConfigManager(IConfiguration configRoot)
    {
        _configRoot = (IConfigurationRoot)configRoot;
    }
    public ApiServerConfig GetApiServerConfig()
    {
        return _configRoot.GetSection(_apiServerConfigKey).Get<ApiServerConfig>() ?? new();
    }

    public void UpdateApiServerConfig(ApiServerConfig apiServerConfig)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), _settingFileName);
        var node = JsonNode.Parse(System.IO.File.ReadAllText(path)) ??
            throw new Exception("Unable to read appsettings.json file.");

        node[_apiServerConfigKey] = JsonNode.Parse(JsonSerializer.Serialize(apiServerConfig));
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(node, new JsonSerializerOptions() { WriteIndented = true }));
        _configRoot.Reload();
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrEmpty(GetApiServerConfig()?.Database?.ConnectionString);
    }
}
