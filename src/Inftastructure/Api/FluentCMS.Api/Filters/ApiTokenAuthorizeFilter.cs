//using FluentCMS.Providers.ApiTokenProviders;
//using Microsoft.AspNetCore.Mvc.Filters;
//using Microsoft.Extensions.DependencyInjection;

//namespace FluentCMS.Core.Api.Filters;

//public class ApiTokenAuthorizeFilter : IAsyncAuthorizationFilter
//{
//    private const string _apiTokenHearKey = "X-API-AUTH";
//    private const string _anyPolicyArea = "Global";
//    private const string _anyPolicyAction = "All Actions";

//    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
//    {
//        var setupService = context.HttpContext.RequestServices.GetRequiredService<ISetupService>();
//        if (!await setupService.IsInitialized())
//            return;

//        // get all PolicyAttributes for the current method
//        var actionPolicies = context.ActionDescriptor.EndpointMetadata.OfType<PolicyAttribute>();

//        // if no policy attributes are found, return
//        if (!actionPolicies.Any())
//            return;

//        // Check API Token
//        var isApiTokenValid = await ValidateApiToken(context, actionPolicies);
//        if (!isApiTokenValid)
//        {
//            context.Result = new ForbidResult();
//            return;
//        }
//    }

//    private static async Task<bool> ValidateApiToken(AuthorizationFilterContext context, IEnumerable<PolicyAttribute> actionPolicies)
//    {
//        var apiTokenProvider = context.HttpContext.RequestServices.GetRequiredService<IApiTokenProvider>();
//        var requestApiToken = context.HttpContext.Request.Headers[_apiTokenHearKey];
//        if (string.IsNullOrEmpty(requestApiToken))
//            return false;

//        var parts = requestApiToken.ToString().Split(':');
//        if (parts.Length != 2)
//            return false;

//        var apiKey = parts[0];
//        var secretKey = parts[1];

//        var checkSignature = apiTokenProvider.Validate(apiKey, secretKey);
//        if (!checkSignature)
//            return false;

//        var apiTokenService = context.HttpContext.RequestServices.GetRequiredService<IApiTokenService>();

//        // Check if the api key is valid
//        var token = await apiTokenService.Validate(apiKey, secretKey);

//        if (token == null)
//            return false;

//        if (token.Policies.Any(x => x.Area == _anyPolicyArea && x.Actions.Contains(_anyPolicyAction)))
//            return true;

//        foreach (var policy in actionPolicies)
//        {
//            var areaPolicyAccess = token.Policies.FirstOrDefault(x => x.Area == policy.Area);
//            if (areaPolicyAccess == null)
//                return false;

//            var hasActionAccess = areaPolicyAccess.Actions.Contains(policy.Action);
//            if (!hasActionAccess)
//                return false;
//        }

//        return true;
//    }
//}

