namespace FluentCMS.Publishing;

/// <summary>What a schedule does when it comes due.</summary>
public enum ScheduleAction
{
    /// <summary>Publishes the draft of the locale.</summary>
    Publish = 1,

    /// <summary>Withdraws the published state of the locale.</summary>
    Unpublish = 2,
}
