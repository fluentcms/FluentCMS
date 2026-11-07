using System.Collections.Concurrent;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace FluentCMS.Data.Tests.Identity;

internal sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(Messages);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}

internal sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;

    public string ApplicationName { get; set; } = "FluentCMS.Data.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

// Stored admin users on the service database, whose roles come from cms_user_role. Email goes to a recorder unless a test
// asks for the default sender of an environment.
internal sealed class IdentityFixture : IDisposable
{
    public const string Password = "Correct-horse-1";

    public static readonly DateTimeOffset Start = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public static readonly Uri AcceptUrl = new("https://cms.example/connect/invitation");

    public IdentityFixture(Action<FluentCmsBuilder>? more = null, string? environment = null, bool activity = false)
    {
        Database = new ServiceDatabase(cms =>
        {
            cms.Activity.Enabled = activity;
            cms.Services.AddScoped<IPrincipalRoles, UserPrincipalRoles>();
            cms.Services.AddSingleton<TimeProvider>(Clock);
            cms.Services.AddSingleton<ILoggerProvider>(Logs);
            if (environment is null)
            {
                cms.Services.AddSingleton<IEmailSender>(Email);
            }
            else
            {
                cms.Services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
            }

            more?.Invoke(cms);
        });
    }

    public ServiceDatabase Database { get; }

    public ManualTimeProvider Clock { get; } = new(Start);

    public RecordingEmailSender Email { get; } = new();

    public CapturedLogs Logs { get; } = new();

    public static CancellationToken Token => ServiceDatabase.Token;

    // The host's own code, which acts as the system.
    public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Database.Database.NewScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, ServiceDatabase.SiteA);
        return await work(scope.ServiceProvider);
    }

    public Task<T> UsersAsync<T>(Func<CmsUserManager, Task<T>> work) =>
        WithAsync(services => work(services.GetRequiredService<CmsUserManager>()));

    public async Task<CmsUser> CreateUserAsync(string name, string? password = Password, params Guid[] roles)
    {
        var user = new CmsUser { UserName = name, Email = $"{name}@example.com" };
        await UsersAsync(async users =>
        {
            Succeeded(password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password));
            if (roles.Length > 0)
            {
                Succeeded(await users.AddToRolesAsync(user, roles));
            }

            return true;
        });

        return user;
    }

    public Task<CmsUser> FindAsync(Guid id) =>
        UsersAsync(async users => (await users.FindByIdAsync(id.ToString("D"))).ShouldNotBeNull());

    // The principal the authorization server will build for a stored user: its id and the management scope, no roles claim.
    public static Principal PrincipalOf(CmsUser user) =>
        new(PrincipalKind.AdminUser, user.Id, user.UserName!, new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });

    public Task<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal) =>
        WithAsync(services => services.GetRequiredService<IPrincipalRoles>().RolesOfAsync(principal, Token).AsTask());

    public List<Invitation> Invitations() =>
        Read(context => context.Set<Invitation>().AsNoTracking().OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList());

    public List<ActivityRow> ActivityRows() =>
        Read(context => context.Set<ActivityRow>().AsNoTracking().OrderBy(row => row.OccurredAt).ThenBy(row => row.Action).ToList());

    public T Read<T>(Func<SystemDbContext, T> read)
    {
        using var scope = Database.Database.NewScope();
        return read(scope.ServiceProvider.GetRequiredService<SystemDbContext>());
    }

    public static string TokenOf(EmailMessage message)
    {
        var link = message.TextBody.Split('\n').Single(line => line.StartsWith(AcceptUrl.AbsoluteUri, StringComparison.Ordinal));
        return Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
    }

    public static void Succeeded(IdentityResult result) =>
        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors.Select(error => $"{error.Code} {error.Description}")));

    public void Dispose() => Database.Dispose();
}
