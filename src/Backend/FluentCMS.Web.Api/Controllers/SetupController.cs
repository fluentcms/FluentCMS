using FluentCMS.Services.Models.Setup;
using System.IO;
using System.Text.Json;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(ISetupService setupService) : BaseGlobalController
{
    public const string AREA = "Setup Management";
    public const string READ = "Read";
    public const string CREATE = "Create";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<bool>> IsInitialized(CancellationToken cancellationToken = default)
    {
        return Ok(await setupService.IsInitialized(cancellationToken));
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        var manifestFilePath = Path.Combine(SetupService.SetupTemplatesFolder, request.Template, SetupService.SetupManifestFile);

        if (!System.IO.File.Exists(manifestFilePath))
            throw new AppException($"{SetupService.SetupManifestFile} doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var setupTemplate = await JsonSerializer.DeserializeAsync<SetupTemplate>(System.IO.File.OpenRead(manifestFilePath), jsonSerializerOptions) ??
               throw new AppException($"Failed to read/deserialize {SetupService.SetupManifestFile}");

        setupTemplate.Url = request.Url;
        setupTemplate.Email = request.Email;
        setupTemplate.Password = request.Password;
        setupTemplate.Username = request.Username;
        setupTemplate.Site.Url = request.Url;
        setupTemplate.Site.Template = request.Template;

        return Ok(await setupService.Start(setupTemplate));
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<string>> GetTemplates(CancellationToken cancellationToken = default)
    {
        var templates = await setupService.GetTemplates(cancellationToken);
        return OkPaged(templates);
    }
}
