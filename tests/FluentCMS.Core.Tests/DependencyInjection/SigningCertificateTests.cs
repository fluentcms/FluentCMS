using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.DependencyInjection;

// On Windows a PKCS#12 key loaded with the default flags is written to the user's key folder and left there when the
// process is killed; an ephemeral key stays in memory and still signs.
public sealed class SigningCertificateTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-signing-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void TheSigningCertificateIsLoadedWithAKeyThatSigns()
    {
        var options = new SigningCertificateOptions { Path = Write("signing.pfx", "signing-password"), Password = "signing-password" };

        using var certificate = options.Load();
        using var rsa = certificate.GetRSAPrivateKey().ShouldNotBeNull();

        var data = "a token"u8.ToArray();
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var verifier = certificate.GetRSAPublicKey()!;
        verifier.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
    }

    [Fact]
    public void OnWindowsTheKeyIsNotWrittenToTheProfile()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows persists the key of a PKCS#12 file.");
        var options = new SigningCertificateOptions { Path = Write("signing.pfx", "signing-password"), Password = "signing-password" };

        using var certificate = options.Load();

        if (OperatingSystem.IsWindows())
        {
            IsEphemeral(certificate).ShouldBeTrue();
        }
    }

    [SupportedOSPlatform("windows")]
    internal static bool IsEphemeral(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        return rsa is RSACng { Key.IsEphemeral: true };
    }

    private string Write(string name, string password)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=FluentCMS test signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(_directory.FullName, name);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
        return path;
    }
}
