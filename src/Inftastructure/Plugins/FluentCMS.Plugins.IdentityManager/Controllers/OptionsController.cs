////using FluentCMS.Options.Services;

////namespace FluentCMS.Plugins.IdentityManager.Controllers;

//[ApiController]
//[Route("api/[controller]/[action]")]
//public class OptionsController(IOptionsService optionsService, IOptionsSnapshot<IdentityOptions> options)
//{
//    [HttpGet]
//    public Task<ApiResult<IdentityOptions>> Get(CancellationToken cancellationToken = default)
//    {
//        return Task.FromResult(new ApiResult<IdentityOptions>(options.Value));
//    }

//    [HttpPut]
//    public async Task<ApiResult<IdentityOptions>> Update([FromBody] IdentityOptions options, CancellationToken cancellationToken = default)
//    {
//        // TODO: validation
//        await optionsService.Update("IdentityOptions", options, cancellationToken);
//        return new ApiResult<IdentityOptions>(options);
//    }
//}
