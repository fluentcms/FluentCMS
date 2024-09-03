using Microsoft.Extensions.Hosting;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Services.Setup.Handlers;

public class SetInitializedHandler(IHostEnvironment hostEnvironment) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.SetInitialized;

    public override async Task<SetupContext> Handle(SetupContext context)
    {
        var appSettingsFilePath = Path.Combine($"appsettings.{hostEnvironment.EnvironmentName}.json");
        var text = await System.IO.File.ReadAllTextAsync(appSettingsFilePath);
        var jsonNode = JsonNode.Parse(text)!;

        // set api token to appsettings.json
        jsonNode!["ServerSettings"]!["IsInitialized"] = true;

        var output = JsonSerializer.Serialize(jsonNode, new JsonSerializerOptions() { WriteIndented = true });
        System.IO.File.WriteAllText(appSettingsFilePath, output);

        return await base.Handle(context);
    }
}
