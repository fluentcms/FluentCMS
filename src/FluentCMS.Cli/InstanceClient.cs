using System.Net;
using System.Net.Http.Headers;

namespace FluentCMS.Cli;

// Reads the management API of a running instance with its key, sent as "Authorization: ApiKey <key>" and never written out.
// Only a JSON answer is the instance's.
internal sealed class InstanceClient(Instance instance, HttpClient http)
{
    private const string Management = "api/management/v1/";

    // The body as it came, byte for byte, so that a snapshot keeps its hash.
    public async Task<byte[]> GetAsync(string path, string permission, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", instance.Token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(CliErrorCodes.InstanceUnreachable, $"{instance} could not be reached: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliException(CliErrorCodes.InstanceUnreachable, $"{instance} did not answer in time");
        }

        using (response)
        {
            if (InstanceAnswer.IsRedirect(response))
            {
                throw new CliException(CliErrorCodes.UnexpectedAnswer, $"{instance} answered {path} with {InstanceAnswer.Redirect(response)}.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new CliException(
                    CliErrorCodes.InstanceRefused,
                    $"{instance} refused the key ({(int)response.StatusCode}); {path} needs a management key with {permission}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CliException(CliErrorCodes.InstanceUnreachable, $"{instance} answered {(int)response.StatusCode} {response.ReasonPhrase} for {path}");
            }

            if (!InstanceAnswer.Is(response, InstanceAnswer.Json))
            {
                throw new CliException(CliErrorCodes.UnexpectedAnswer, $"{instance} answered {path} with {InstanceAnswer.MediaTypeOf(response)}, not JSON; is it a FluentCMS instance?");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
    }

    // Under the instance's path base, if its address has one.
    private Uri Address(string path)
    {
        var root = instance.Url.AbsoluteUri.EndsWith('/') ? instance.Url : new Uri($"{instance.Url.AbsoluteUri}/");
        return new Uri(root, Management + path);
    }
}
