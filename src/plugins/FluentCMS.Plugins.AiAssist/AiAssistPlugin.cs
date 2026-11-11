using FluentCMS.Authorization;
using FluentCMS.Plugins;
using FluentCMS.Plugins.AiAssist;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsPlugin(typeof(AiAssistPlugin))]

namespace FluentCMS.Plugins.AiAssist;

// AI assist of spec 5.5: opt-in actions on fields, whose answers are suggestions, over the provider each site chooses,
// with a usage log. It reads content and media through their services as the caller and writes nothing to content.
[FluentCmsPlugin("ai", SdkRange = "[1.0.0,2.0.0)")]
public sealed class AiAssistPlugin : IPlugin
{
    public const string Prefix = "/ai";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.Section)
            .Validate(
                options => options.IsValid(),
                $"{AiOptions.Section}: AllowedEndpoints holds absolute http or https URLs with no user, query or fragment, MaxInputLength is from 1 to 1000000 and UsageRetention is positive.")
            .ValidateOnStart();
        plugin.Services.AddScoped<AiAssistService>();
        plugin.Services.AddScoped<AiUsageService>();
        plugin.Services.AddSingleton<AiCallLimits>();
        plugin.Services.AddHostedService<AiUsageRetentionSchedule>();

        plugin
            .AddProviderSlot<IAiClientFactory, AiClientFactory>()
            .AddPermissions<AiPermissions>()
            .AddSettings<AiSettings>()
            .AddModel<AiUsageModel>()
            .AddMigrations(typeof(AiAssistPlugin).Assembly)
            .AddJob<AiUsageRetentionJob, AiUsageRetentionPayload>(AiUsageRetentionJob.Type, AiUsageRetentionJob.AttemptTimeout)
            .AddErrorCode(AiErrorCodes.InvalidRequest, 422, "Invalid AI request")
            .AddErrorCode(AiErrorCodes.UnknownAction, 404, "Unknown AI action")
            .AddErrorCode(AiErrorCodes.NoProvider, 409, "No AI provider")
            .AddErrorCode(AiErrorCodes.FieldNotOptedIn, 403, "Field not opted in to AI actions")
            .AddErrorCode(AiErrorCodes.EndpointNotAllowed, 409, "AI endpoint not allowed")
            .AddErrorCode(AiErrorCodes.ProviderNotConfigured, 409, "AI provider not configured")
            .AddErrorCode(AiErrorCodes.InputTooLong, 413, "AI input too long")
            .AddErrorCode(AiErrorCodes.CallLimitExceeded, 429, "Too many AI calls")
            .AddErrorCode(AiErrorCodes.TimedOut, 504, "AI provider timed out")
            .AddErrorCode(AiErrorCodes.ProviderFailed, 502, "AI provider failed")
            .AddErrorCode(AiErrorCodes.AnswerUnusable, 502, "AI answer not usable")
            .AddErrorCode(AiErrorCodes.ImageNotAvailable, 422, "Image not available");
    }

    public void Configure(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapManagement(Prefix).MapAiEndpoints();
    }
}

public sealed class AiPermissions : IPermissionProvider
{
    // Running AI actions on the fields a site opted in.
    public const string Use = "ai.use";

    // Reading the usage log of a site.
    public const string ReadUsage = "ai.usage.read";

    public IEnumerable<Permission> GetPermissions() =>
    [
        new Permission(Use, "Run AI actions on the fields a site opted in."),
        new Permission(ReadUsage, "Read the AI usage log of a site."),
    ];
}
