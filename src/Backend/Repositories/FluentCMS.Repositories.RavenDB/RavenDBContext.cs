using System.Security.Cryptography.X509Certificates;

namespace FluentCMS.Repositories.RavenDB;

public interface IRavenDBContext
{
    IDocumentStore Store { get; }
}

public class RavenDBContext : IRavenDBContext
{
    public RavenDBContext(RavenDBOptions<RavenDBContext> options)
    {
        // Validate input arguments to ensure they are not null.
        ArgumentNullException.ThrowIfNull(options, nameof(options));
        ArgumentNullException.ThrowIfNull(options.URL, nameof(options.URL));
        ArgumentNullException.ThrowIfNull(options.DatabaseName, nameof(options.DatabaseName));
        ArgumentNullException.ThrowIfNull(options.CertificatePath, nameof(options.CertificatePath));

        if (System.IO.File.Exists(options.CertificatePath) == false)
            throw new ArgumentException($"Certificate '{options.CertificatePath}' not found!");

        Store = new DocumentStore()
        {
            Urls = [options.URL],
            Database = options.DatabaseName,
            Certificate = new X509Certificate2(options.CertificatePath),

            // Set conventions as necessary (optional)
            Conventions =
            {
                DisposeCertificate = false,
                MaxNumberOfRequestsPerSession = 10,
                UseOptimisticConcurrency = true,
                AddIdFieldToDynamicObjects = false,
                FindIdentityProperty = memberInfo => memberInfo.Name == "RavenId",
            },
        }.Initialize();
    }

    public IDocumentStore Store { get; private set; }
}
