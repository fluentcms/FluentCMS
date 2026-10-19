using System.Globalization;
using FluentCMS.Core.Models;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Models;

internal sealed class ModelProfiles(IOptions<ModelOptions> options) : IModelProfiles
{
    private readonly Dictionary<string, ModelProfile> _byName = options.Value.Profiles
        .ToDictionary(pair => pair.Key, pair => ToProfile(pair.Key, pair.Value), StringComparer.Ordinal);

    public IReadOnlyList<ModelProfile> All => _byName.Values.OrderBy(profile => profile.Name, StringComparer.Ordinal).ToList();

    public ModelProfile? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    // Validated at startup (ModelOptionsValidation), so a profile here is well formed.
    private static ModelProfile ToProfile(string name, ModelProfileOptions profile)
    {
        var endpoint = profile.Endpoint is { Length: > 0 } given ? new Uri(given) : profile.DefaultEndpoint();

        return new ModelProfile(name, profile.Provider!, endpoint, profile.Model!)
        {
            InputCostPerMillionTokens = profile.InputCostPerMillionTokens,
            OutputCostPerMillionTokens = profile.OutputCostPerMillionTokens,
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
        };
    }
}

// A profile the host cannot use stops it at startup with what to change, rather than failing at the first check.
internal sealed class ModelOptionsValidation : IValidateOptions<ModelOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelOptions options)
    {
        var failures = new List<string>();

        if (options.PromptRetention <= TimeSpan.Zero)
        {
            failures.Add("Models:PromptRetention must be a positive duration, such as 90.00:00:00.");
        }

        foreach (var (profile, settings) in options.Profiles)
        {
            var prefix = $"Models:Profiles:{profile}";
            if (settings.Provider is null || !ModelProviders.All.Contains(settings.Provider))
            {
                failures.Add($"{prefix}:Provider must be one of {string.Join(", ", ModelProviders.All)}.");
            }

            if (string.IsNullOrWhiteSpace(settings.Model))
            {
                failures.Add($"{prefix}:Model must name the model, such as the one the provider lists.");
            }

            if (settings.Endpoint is { Length: > 0 } endpoint
                && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            {
                failures.Add($"{prefix}:Endpoint must be an absolute http or https URL, such as http://localhost:11434/v1.");
            }

            if (settings.InputCostPerMillionTokens < 0 || settings.OutputCostPerMillionTokens < 0)
            {
                failures.Add($"{prefix}: token prices cannot be negative.");
            }

            if (settings.MaxOutputTokens < 1)
            {
                failures.Add($"{prefix}:MaxOutputTokens must be at least 1.");
            }

            if (settings.Timeout <= TimeSpan.Zero)
            {
                failures.Add($"{prefix}:Timeout must be a positive duration, such as {TimeSpan.FromSeconds(60).ToString("c", CultureInfo.InvariantCulture)}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
