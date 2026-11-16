using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FluentCMS.Admin.EndToEnd;

// The certificates of one run's throwaway host, made by the run as files and deleted with it: nothing is added to a trust
// store, no certificate store is used and no machine setting changes. One is for Kestrel's https listener on the loopback
// address, and one signs the tokens and protects the key ring, so that the host starts in Production without the
// evaluation opt-ins. Their password is made per run. On Windows the host imports the files with default flags, so their
// private keys stay in the user's key folder (%APPDATA%\Microsoft\Crypto\Keys) after the run, until #3212.
internal sealed class TestCertificates
{
    private TestCertificates(X509Certificate2 server, string serverPath, string signingPath, string password)
    {
        Server = server;
        ServerPath = serverPath;
        SigningPath = signingPath;
        Password = password;
    }

    public X509Certificate2 Server { get; }

    public string ServerPath { get; }

    public string SigningPath { get; }

    public string Password { get; }

    public static TestCertificates Create(string directory)
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var now = DateTimeOffset.UtcNow;

        using var serverKey = RSA.Create(2048);
        var request = new CertificateRequest($"CN={IPAddress.Loopback}", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var server = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        var serverPath = Path.Combine(directory, "server.pfx");
        File.WriteAllBytes(serverPath, server.Export(X509ContentType.Pkcs12, password));

        using var signingKey = RSA.Create(2048);
        using var signing = new CertificateRequest("CN=FluentCMS end-to-end signing", signingKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        var signingPath = Path.Combine(directory, "signing.pfx");
        File.WriteAllBytes(signingPath, signing.Export(X509ContentType.Pkcs12, password));

        return new TestCertificates(X509CertificateLoader.LoadCertificate(server.RawData), serverPath, signingPath, password);
    }
}
