using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// A reset and the verification mail again go out from jobs of the mail queue. The hosts of the tests give the queue no
// worker and run its jobs here, so that a test knows they ran before it reads the mail.
internal static class AccountMailJobs
{
    public static void RunByTest(FluentCmsBuilder cms) => cms.Jobs.Options.Queues[AccountMail.Queue] = 0;

    public static async Task<int> RunAsync(FluentCmsTestHost host)
    {
        var ran = 0;
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(AccountMail.Queue, "test", TestContext.Current.CancellationToken))
            {
                return ran;
            }

            ran++;
        }
    }

    public static async Task<int> PendingAsync(FluentCmsTestHost host, string type)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking()
            .CountAsync(job => job.Type == type && job.Queue == AccountMail.Queue && job.Status == JobStatus.Pending, TestContext.Current.CancellationToken);
    }
}

// Holds every mail until the test lets it go, so that a test sees what waits for a send.
internal sealed class BlockingEmailSender : IEmailSender
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _sent;

    public TaskCompletionSource<EmailMessage> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Sent => Volatile.Read(ref _sent);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult(message);
        await _release.Task.WaitAsync(cancellationToken);
        Interlocked.Increment(ref _sent);
    }

    public void Release() => _release.TrySetResult();
}
