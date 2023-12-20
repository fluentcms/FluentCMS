using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// Extension methods for adding configuration settings and retrieving configured instances.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds configuration files from a specified folder to the configuration builder.
    /// </summary>
    /// <param name="configBuilder">The configuration builder to add the configuration files to.</param>
    /// <param name="env">The hosting environment information.</param>
    /// <param name="configFolder">The folder relative to the content root path from which to load configuration files. Defaults to "/Config/".</param>
    /// <returns>The updated configuration builder.</returns>
    public static IConfigurationBuilder AddConfig(this IConfigurationBuilder configBuilder, IHostEnvironment env, string configFolder = "/Config/")
    {
        var folderName = env.ContentRootPath + configFolder;

        // Set the base path for the configuration folder
        configBuilder.SetBasePath(folderName);

        foreach (var filename in Directory.GetFiles(folderName))
        {
            // Load only JSON files
            if (Path.GetExtension(filename) != ".json")
                continue;

            // Accept only root config files (not environment-specific)
            if (Path.GetFileName(filename).Split(".").Length != 2)
                continue;

            // Add the configuration file
            configBuilder.AddJsonFile(filename, optional: true, reloadOnChange: true)
                .AddJsonFile($"{Path.GetFileNameWithoutExtension(filename)}.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
        }

        return configBuilder;
    }

    /// <summary>
    /// Retrieves a configured instance of the specified type from the configuration section.
    /// </summary>
    /// <typeparam name="T">The type of the configuration instance to retrieve.</typeparam>
    /// <param name="configuration">The configuration from which to retrieve the instance.</param>
    /// <param name="sectionName">The name of the configuration section.</param>
    /// <returns>The configured instance of type <typeparamref name="T"/>; or null if the section is not found.</returns>
    public static T? GetInstance<T>(this IConfiguration configuration, string sectionName)
    {
        return configuration.GetSection(sectionName).Get<T>();
    }
}
