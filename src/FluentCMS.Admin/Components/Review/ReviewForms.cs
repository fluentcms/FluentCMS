namespace FluentCMS.Admin.Components.Review;

// The posted fields of a review action on the set view. Seen names the last move of the set the page was rendered for, so an
// action is refused once the set has moved on, rather than applied to a set the reviewer has not seen.
public class ReviewForm
{
    public string? Seen { get; set; }
}

// The directory checks the reason, so a blank one is refused there (CHG-08).
public sealed class RejectForm : ReviewForm
{
    public string? Reason { get; set; }
}
