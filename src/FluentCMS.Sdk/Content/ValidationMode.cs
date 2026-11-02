namespace FluentCMS.Content;

/// <summary>Which rules a validation run enforces.</summary>
public enum ValidationMode
{
    /// <summary>A draft save: <c>Required</c> and <c>RequiredWhen</c> are skipped so incomplete work can be saved.</summary>
    Draft = 1,

    /// <summary>A publish: every rule is enforced.</summary>
    Publish = 2,
}
