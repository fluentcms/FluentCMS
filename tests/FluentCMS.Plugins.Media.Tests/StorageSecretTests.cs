using System.Net;
using FluentCMS.Plugins.Media.AzureBlob;
using FluentCMS.Plugins.Media.S3;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.StorageServices;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// A storage that refuses or does not answer: its credentials are in no log line, in neither health answer, in the startup
// report and in no error answer of an upload.
[Collection(StorageServicesGroup.Name)]
public sealed class StorageSecretTests(StorageServices services) : IDisposable
{
    private readonly DirectoryInfo _local = Directory.CreateTempSubdirectory("fluentcms-storage-");

    public static TheoryData<string> Refusals => ["s3-unreachable", "s3-wrong-secret", "azure-wrong-key", "azure-wrong-sas"];

    public void Dispose() => _local.Delete(recursive: true);

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task NoLogLineReportOrAnswerHoldsACredentialOfTheStorage(string refusal)
    {
        var (use, secrets) = await RefusingAsync(refusal);
        var logs = new CapturedLogs();
        await using var host = StorageHosts.Create(_local, StorageHosts.SigningKey(), use, logs: logs);
        using var admin = await StorageHosts.AdminAsync(host);
        using var anonymous = host.CreateClient();
        List<string> answers = [];

        using (var ready = await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative), Token))
        {
            ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            answers.Add(await ready.Content.ReadAsStringAsync(Token));
        }

        var check = await StorageProviderTests.StorageCheckAsync(host);
        check["status"]!.GetValue<string>().ShouldBe("Unhealthy");
        answers.Add(check.ToJsonString());
        answers.Add((await GetAsync(admin, "/api/management/v1/system/health")).ToJsonString());
        answers.Add((await GetAsync(admin, "/api/management/v1/system/diagnostics")).ToJsonString());
        using (var upload = await UploadAsync(admin, null, ("photo.png", Bands(40, 20))))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            answers.Add(await upload.Content.ReadAsStringAsync(Token));
        }

        logs.Lines.ShouldNotBeEmpty();
        foreach (var secret in secrets)
        {
            foreach (var text in answers.Concat(logs.Lines))
            {
                text.ShouldNotContain(secret, Case.Insensitive);
            }
        }
    }

    private async Task<(Action<MediaPlugin> Use, string[] Secrets)> RefusingAsync(string refusal)
    {
        switch (refusal)
        {
            case "s3-unreachable":
            case "s3-wrong-secret":
                var bucket = refusal == "s3-unreachable"
                    ? new S3StorageOptions { Endpoint = new Uri("http://127.0.0.1:1"), ForcePathStyle = true, Bucket = "media", AccessKeyId = "AKIAFLUENTCMSTESTKEY" }
                    : await services.BucketAsync();
                bucket.SecretAccessKey = "wJalrXUtnFEMI-fluentcms-secret-access-key";
                bucket.SessionToken = "IQoJb3JpZ2luX2VjEFluentCmsSessionToken";
                return (media => media.UseS3(Section(bucket)), [bucket.SecretAccessKey, bucket.SessionToken, bucket.AccessKeyId!]);
            case "azure-wrong-key":
                var key = Convert.ToBase64String("fluentcms-not-the-azurite-account-key-0123456789"u8);
                var wrongKey = await services.ContainerAsync();
                wrongKey.ConnectionString = wrongKey.ConnectionString!.Replace(AccountKeyOf(wrongKey.ConnectionString), key, StringComparison.Ordinal);
                return (media => media.UseAzureBlob(Section(wrongKey)), [key, wrongKey.ConnectionString]);
            default:
                var wrongSas = await services.ContainerAsync(sas: true);
                var signature = Uri.EscapeDataString(Convert.ToBase64String("fluentcms-forged-sas-signature-0123456789ab"u8));
                var query = string.Join('&', wrongSas.ContainerUrl!.Query.TrimStart('?').Split('&').Select(pair => pair.StartsWith("sig=", StringComparison.Ordinal) ? "sig=" + signature : pair));
                wrongSas.ContainerUrl = new Uri($"{wrongSas.ContainerUrl.GetLeftPart(UriPartial.Path)}?{query}");
                return (media => media.UseAzureBlob(Section(wrongSas)), [signature, Uri.UnescapeDataString(signature)]);
        }
    }

    private static string AccountKeyOf(string connectionString) =>
        connectionString.Split(';').Single(part => part.StartsWith("AccountKey=", StringComparison.Ordinal))["AccountKey=".Length..];
}
