using System;
using System.Linq;

namespace FluentCMS.Repositories.RavenDB;

public class RavenDBOptions<TContext> where TContext : IRavenDBContext
{
    public RavenDBOptions(string connectionString)
    {
        // Guard clause to ensure the connection string is not null or empty
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        // Split connection string by ; and = to get name value pairs
        var value = connectionString.Split(';')
                        .Select(x => x.Split('='))
                        .ToDictionary(s => s.First(), s => s.Last());

        if (value.ContainsKey("URL") == false || string.IsNullOrWhiteSpace(value["URL"]))
            throw new ArgumentException("URL is missing or empty", nameof(URL));

        if (value.ContainsKey("DatabaseName") == false || string.IsNullOrWhiteSpace(value["DatabaseName"]))
            throw new ArgumentException("DatabaseName is missing or empty", nameof(DatabaseName));

        if (value.ContainsKey("CertificatePath") == false || string.IsNullOrWhiteSpace(value["CertificatePath"]))
            throw new ArgumentException("CertificatePath is missing or empty", nameof(CertificatePath));

        URL = value["URL"];
        DatabaseName = value["DatabaseName"];
        CertificatePath = value["CertificatePath"];
    }

    public string URL { get; }
    
    /// <summary>
    /// Name of database to use
    /// </summary>
    public string DatabaseName { get; }
    
    /// <summary>
    /// Path to pfx certificate
    /// </summary>
    public string CertificatePath { get; }
}
