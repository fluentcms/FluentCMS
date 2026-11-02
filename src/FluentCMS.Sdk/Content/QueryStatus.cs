namespace FluentCMS.Content;

/// <summary>Which state of the entries a <see cref="ContentQuery"/> reads.</summary>
public enum QueryStatus
{
    /// <summary>The published state (<c>published</c>).</summary>
    Published = 0,

    /// <summary>The draft state (<c>draft</c>).</summary>
    Draft = 1,
}
