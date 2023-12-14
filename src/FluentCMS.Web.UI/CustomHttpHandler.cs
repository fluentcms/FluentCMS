using Blazored.LocalStorage;

namespace JWT.Auth.BlazorUI.Shared.Providers
{
    public class CustomHttpHandler: DelegatingHandler
    {
        private readonly ILocalStorageService _localStorageService;
        public CustomHttpHandler(ILocalStorageService localStorageService)
        {
            _localStorageService = localStorageService;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if(request.RequestUri.AbsolutePath.ToLower().Contains("login") ||
                request.RequestUri.AbsolutePath.ToLower().Contains("register"))
            {
                return await base.SendAsync(request, cancellationToken);
            }

            var token = await _localStorageService.GetItemAsync<string>("jwt-access-token");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
