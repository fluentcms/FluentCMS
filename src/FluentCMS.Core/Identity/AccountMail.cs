using FluentCMS.Jobs;

namespace FluentCMS.Core.Identity;

// The mail a visitor asks for with only an address, a reset of the admin pool or of the member pool and the verification
// mail of a member again, goes out from a job on a queue of its own, after the answer. The request only checks the
// address's form and enqueues the job, whether or not the address is anyone's, so the time of the answer tells nothing;
// the job finds the account, makes the token and sends. A job fails only before it sends, so a retry never mails twice.
internal static class AccountMail
{
    public const string Queue = "mail";

    public const int Workers = 2;

    public static readonly JobOptions Options = new() { Queue = Queue };
}
