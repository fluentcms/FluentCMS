using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Services.MessageHandlers;

public class ApiTokenMessageHandler(IApiTokenService apiTokenService) : IMessageHandler<SetupTemplate>
{
    public async Task Handle(Message<SetupTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupStarted:
                var apiToken = await apiTokenService.Create(GetDefaultApiToken(), cancellationToken);
                var appSettingsFilePath = Path.Combine($"appsettings.json");
                var text = System.IO.File.ReadAllText(appSettingsFilePath);
                var jsonNode = JsonNode.Parse(text)!;
                jsonNode!["ClientSettings"]!["Key"] = apiToken.Key + ":" + apiToken.Secret;

                var output = JsonSerializer.Serialize(jsonNode, new JsonSerializerOptions() { WriteIndented = true });
                System.IO.File.WriteAllText(appSettingsFilePath, output);

                break;

            default:
                break;
        }
    }

    private static ApiToken GetDefaultApiToken()
    {
        return new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = "Full Access",
            Description = "Full Access Token",
            ExpireAt = null,
            Policies = [new Policy { Area = "Global", Actions = ["All Actions"] }],
            Enabled = true
        };
    }
}
