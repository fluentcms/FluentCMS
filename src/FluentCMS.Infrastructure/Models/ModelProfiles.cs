using System.Globalization;
using FluentCMS.Core.Models;
using FluentCMS.Core.Schema;
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
    private const int MaxOutputTokens = 200_000;

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
            if (!SchemaNames.IsSlug(profile))
            {
                failures.Add($"{prefix}: a profile is named in lowercase words joined by hyphens, such as risk-scoring.");
            }

            if (settings.Provider is null || !ModelProviders.All.Contains(settings.Provider))
            {
                failures.Add($"{prefix}:Provider must be one of {string.Join(", ", ModelProviders.All)}.");
            }

            if (string.IsNullOrWhiteSpace(settings.Model) || settings.Model.Length > ModelCall.MaxNameLength)
            {
                failures.Add($"{prefix}:Model must name the model as the provider lists it, in at most {ModelCall.MaxNameLength} characters.");
            }

            if (settings.Provider == ModelProviders.Anthropic && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                failures.Add($"{prefix}:ApiKey is required for Anthropic, so the SDK never falls back to credentials it finds on the machine.");
            }

            if (settings.Endpoint is { Length: > 0 } endpoint && !IsEndpoint(endpoint))
            {
                failures.Add($"{prefix}:Endpoint must be an absolute https URL, or http to this machine, such as http://localhost:11434/v1.");
            }

            if (settings.InputCostPerMillionTokens < 0 || settings.OutputCostPerMillionTokens < 0)
            {
                failures.Add($"{prefix}: token prices cannot be negative.");
            }

            if (settings.MaxOutputTokens is < 1 or > MaxOutputTokens)
            {
                failures.Add($"{prefix}:MaxOutputTokens must be from 1 to {MaxOutputTokens}.");
            }

            if (settings.Temperature is < 0 or > 2)
            {
                failures.Add($"{prefix}:Temperature must be from 0 to 2, or left out for the provider's default.");
            }

            if (settings.Timeout < TimeSpan.FromSeconds(1) || settings.Timeout > TimeSpan.FromHours(1))
            {
                failures.Add($"{prefix}:Timeout must be from 1 second to 1 hour, such as {TimeSpan.FromSeconds(60).ToString("c", CultureInfo.InvariantCulture)}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // A key travels in every request, so it goes over plain http only to a server on this machine.
    private static bool IsEndpoint(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }
}
