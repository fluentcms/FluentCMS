namespace FluentCMS.Core.Storage;

// The values of the state column. They are shorts because a property bag holds the boxed value,
// and EF casts it back to the type of the column.
public static class ContentState
{
    public const short Draft = 0;

    public const short Published = 1;
}
