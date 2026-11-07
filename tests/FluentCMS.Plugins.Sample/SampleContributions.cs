using System.Text.Json.Nodes;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using FluentCMS.Settings;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Sample;

public sealed class SampleNote
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SampleModel : IPluginModelContributor
{
    public const string Table = "cms_sample_note";

    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SampleNote>(note =>
        {
            note.ToTable(Table);
            note.HasKey(item => item.Id);
            note.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            note.Property(item => item.Text).HasColumnName("text").HasMaxLength(200).IsRequired();
            note.Property(item => item.CreatedAt).HasColumnName("created_at");
            note.HasIndex(item => item.Text).IsUnique().HasDatabaseName("ux_cms_sample_note_text");
        });
}

public sealed class SamplePermissions : IPermissionProvider
{
    public const string NotesManage = "sample.notes.manage";

    public IEnumerable<Permission> GetPermissions() => [new Permission(NotesManage, "Add and remove sample notes.")];
}

[Settings("sample")]
public sealed class SampleSettings
{
    public string Greeting { get; set; } = "hello";

    public int MaxNotes { get; set; } = 10;
}

public sealed record SampleNotePayload(string Text);

public sealed record SampleNoteAdded(Guid NoteId, string Text) : CmsEvent;

// Adds a note and raises the event that the outbox hands to SampleNoteHandler and to the activity log.
public sealed class SampleNoteJob(PluginDbContext<SamplePlugin> database, IEventBus events, TimeProvider time) : IJob<SampleNotePayload>
{
    public const string Type = "sample.note";

    public async Task ExecuteAsync(SampleNotePayload payload, JobContext context)
    {
        var note = new SampleNote { Id = Guid.CreateVersion7(), Text = payload.Text, CreatedAt = time.GetUtcNow() };
        database.Add(note);
        await database.SaveChangesAsync(context.CancellationToken);
        await events.PublishAsync(new SampleNoteAdded(note.Id, note.Text), context.CancellationToken);
    }
}

public sealed class SampleNoteHandler(PluginCalls? calls = null) : IOutboxEventHandler<SampleNoteAdded>
{
    public ValueTask HandleAsync(SampleNoteAdded message, EventDelivery delivery, CancellationToken cancellationToken)
    {
        calls?.Add($"sample.outbox:{message.Text}");
        return ValueTask.CompletedTask;
    }
}

public sealed class SampleNoteSeenHandler(PluginCalls? calls = null) : IEventHandler<SampleNoteAdded>
{
    public ValueTask HandleAsync(SampleNoteAdded message, CancellationToken cancellationToken)
    {
        calls?.Add($"sample.event:{message.Text}");
        return ValueTask.CompletedTask;
    }
}

// A short text taken from another field of the same type, which the setting "of" names.
public sealed class ExcerptFieldType : IFieldType
{
    public const string TypeName = "sampleExcerpt";
    public const string Of = "of";

    public string Name => TypeName;

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } =
        [new FieldSettingDescriptor { Key = Of, Kind = FieldSettingKind.FieldReference }];

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value?.ToString();
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string" };
}
