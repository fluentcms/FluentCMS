namespace FluentCMS.Admin.Forms;

/// <summary>A form with changes that are not saved yet, which <see cref="IFormDraftStore.Track"/> keeps until it is saved.</summary>
public interface IUnsavedForm
{
    /// <summary>The form's state now, which the store writes when the session ends.</summary>
    /// <returns>The draft of the form.</returns>
    FormDraft CaptureDraft();
}
