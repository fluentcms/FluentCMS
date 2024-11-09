namespace FluentCMS.Web.ApiClients;

public partial interface IFileClient : IApiClient
{
    Task<HttpResponseMessage> DownloadGetResponseMessageAsync(string? url, CancellationToken cancellationToken = default);
}

public partial class FileClient
{
    public async Task<HttpResponseMessage> DownloadGetResponseMessageAsync(string? url, CancellationToken cancellationToken = default)
    {
        var disposeClient_ = false;
        try
        {
            using var request_ = new HttpRequestMessage();

            request_.Method = new HttpMethod("GET");

            var urlBuilder_ = new System.Text.StringBuilder();

            // Operation Path: "api/File/Download"
            urlBuilder_.Append("api/File/Download");
            urlBuilder_.Append('?');
            if (url != null)
            {
                var htmlDecodedUrl = System.Net.WebUtility.HtmlDecode(url);
                urlBuilder_.Append(Uri.EscapeDataString("url")).Append('=').Append(Uri.EscapeDataString(htmlDecodedUrl));
            }
            else
            {
                urlBuilder_.Length--;
            }

            var url_ = urlBuilder_.ToString();
            request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);

            return await _httpClient.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (disposeClient_)
                _httpClient.Dispose();
        }
    }
}
