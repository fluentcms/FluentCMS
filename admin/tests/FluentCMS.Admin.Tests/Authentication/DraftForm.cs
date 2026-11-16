using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FluentCMS.Admin.Tests.Authentication;

// The form store as a content form of A2 will use it: the form reads the entry, takes the user's draft of it when there is
// one, with the version the draft was based on, tracks itself while it has changes, and removes the draft once saved.
internal sealed class DraftForm : ComponentBase, IUnsavedForm, IDisposable
{
    public const string Type = "article";
    public const string Locale = "en";

    private EntryRead? read;
    private JsonObject fields = [];
    private IDisposable? tracking;

    [Inject]
    public required ManagementClient Client { get; set; }

    [Inject]
    public required IFormDraftStore Drafts { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    public bool Restored { get; private set; }

    private FormDraftKey Key => new(Site: null, Type, Id, Locale);

    public FormDraft CaptureDraft() => new(Key, read!.EntityTag, fields.DeepClone().AsObject());

    public void Dispose() => tracking?.Dispose();

    protected override async Task OnInitializedAsync()
    {
        read = await Client.GetEntryAsync(Type, Id, Locale, CancellationToken.None);
        fields = read.Entry!.Fields.DeepClone().AsObject();
        if (await Drafts.FindAsync(Key, CancellationToken.None) is { } draft)
        {
            fields = draft.Fields.DeepClone().AsObject();
            read = read with { EntityTag = draft.BaseVersion! };
            tracking = Drafts.Track(this);
            Restored = true;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "class", "title");
        builder.AddAttribute(2, "value", fields["title"]?.GetValue<string>());
        builder.AddAttribute(3, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, Change));
        builder.CloseElement();
        builder.OpenElement(4, "button");
        builder.AddAttribute(5, "class", "save");
        builder.AddAttribute(6, "onclick", EventCallback.Factory.Create(this, SaveAsync));
        builder.CloseElement();
    }

    private void Change(ChangeEventArgs change)
    {
        fields["title"] = change.Value?.ToString();
        tracking ??= Drafts.Track(this);
    }

    private async Task SaveAsync()
    {
        read = await Client.SaveEntryAsync(Type, Id, fields, read!, CancellationToken.None);
        tracking?.Dispose();
        tracking = null;
        await Drafts.RemoveAsync(Key, CancellationToken.None);
    }
}
