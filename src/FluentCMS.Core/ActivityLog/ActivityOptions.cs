using Cronos;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.ActivityLog;

public sealed class ActivityOptions
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    // Whether the CMS writes a row for the events it raises and prunes the log. A host that keeps its own record turns it off.
    public bool Enabled { get; set; } = true;

    // How long a row stays. A site in SiteRetention keeps its rows for its own time; the instance-wide rows, which
    // belong to no site, always use this.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    // By site slug, until sites have settings of their own to hold it.
    public Dictionary<string, TimeSpan> SiteRetention { get; } = new(StringComparer.OrdinalIgnoreCase);

    // The most rows one delete of the retention job takes, so that a long backlog is not removed in one statement.
    public int PruneBatchSize { get; set; } = 1000;

    // When the retention job runs, as a cron expression in UTC.
    public string PruneSchedule { get; set; } = "30 3 * * *";

    internal void Validate()
    {
        Require(Retention > TimeSpan.Zero, $"{nameof(Retention)} must be positive.");
        foreach (var (site, retention) in SiteRetention)
        {
            Require(retention > TimeSpan.Zero, $"The retention of the site '{site}' must be positive.");
        }

        Require(PruneBatchSize >= 1, $"{nameof(PruneBatchSize)} is {PruneBatchSize}; it must be at least 1.");
        Require(IsCron(PruneSchedule), $"{nameof(PruneSchedule)} '{PruneSchedule}' is not a cron expression that ever occurs.");
    }

    private static bool IsCron(string? cron)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(cron) && CronExpression.Parse(cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) is not null;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(ActivityErrorCodes.InvalidOptions, message);
        }
    }
}
