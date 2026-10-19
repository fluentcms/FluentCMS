namespace FluentCMS.Core.Security;

// Encrypts a secret the product has to read back, such as a webhook's signing secret, so the database never holds it in plain text
// (SEC-07). The host implements it with its Data Protection key ring; the purpose keeps one kind of secret from opening another.
public interface ISecretProtector
{
    string Protect(string purpose, string secret);

    string Unprotect(string purpose, string protectedSecret);
}
