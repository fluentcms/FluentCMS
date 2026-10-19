using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.Infrastructure.Content;

internal static class EntriesInReview
{
    // The entries a submitted set holds that is neither published nor rejected yet (CNT-01); read, never stored, so a second set in
    // review keeps an entry there however the first one ends.
    public static IQueryable<Guid> Ids(FluentCmsDbContext context)
    {
        var review = EntryStates.Review.ToList();

        return
            from operation in context.ChangeSetOperations
            join set in context.ChangeSets on operation.ChangeSetId equals set.Id
            where (operation.ItemKind == ItemKind.Entry || operation.ItemKind == ItemKind.Entity) && review.Contains(set.State)
            select operation.ItemId;
    }
}
