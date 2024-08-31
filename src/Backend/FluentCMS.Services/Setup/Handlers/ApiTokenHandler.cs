using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Services.Setup.Handlers;

public class ApiTokenHandler(IApiTokenService apiTokenService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.ApiToken;

    public async override Task<SetupContext> Handle(SetupContext context)
    {
        var appSettingsFilePath = Path.Combine("appsettings.json");
        var text = System.IO.File.ReadAllText(appSettingsFilePath);
        var jsonNode = JsonNode.Parse(text)!;

        // Creating full access api token 
        ApiToken apiToken = await CreateDefaultApiToken();

        // set api token to appsettings.json
        jsonNode!["ApiSettings"]!["Key"] = apiToken.Key + ":" + apiToken.Secret;

        var output = JsonSerializer.Serialize(jsonNode, new JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText(appSettingsFilePath, output);

        return await base.Handle(context);
    }

    private async Task<ApiToken> CreateDefaultApiToken()
    {
        var apiToken = new ApiToken
        {
            Name = "Full Access",
            Description = "Full Access Token",
            ExpireAt = null,
            Policies = [new Policy { Area = "Global", Actions = ["All Actions"] }],
            Enabled = true
        };

        await apiTokenService.Create(apiToken);
        return apiToken;
    }
}
