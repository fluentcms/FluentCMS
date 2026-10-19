using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Jobs;

internal sealed class JobConfiguration : IEntityTypeConfiguration<JobRow>
{
    public const int MaxTypeLength = 128;
    public const int MaxMessageLength = 512;

    public void Configure(EntityTypeBuilder<JobRow> row)
    {
        row.ToTable("Jobs", table => table.HasCheckConstraint("CK_Jobs_Attempts", "\"Attempts\" >= 0 AND \"MaxAttempts\" >= 1"));
        row.HasKey(job => job.Id);
        row.Property(job => job.Id).ValueGeneratedNever();
        row.Property(job => job.Type).HasMaxLength(MaxTypeLength).IsRequired();
        row.Property(job => job.Payload).IsRequired();
        row.Property(job => job.SubjectType).HasMaxLength(64);
        row.Property(job => job.InitiatorKind).HasMaxLength(16);
        row.Property(job => job.InitiatorName).HasMaxLength(256);
        row.Property(job => job.LeaseOwner).HasMaxLength(256);
        row.Property(job => job.ProgressMessage).HasMaxLength(MaxMessageLength);

        // Stored as a name, so the table reads without the enum and a reordered enum cannot change stored meaning.
        row.Property(job => job.State).HasConversion<string>().HasMaxLength(16);

        // Workers look for due jobs by state and time; tools and the desk look up the jobs of one item.
        row.HasIndex(job => new { job.State, job.ScheduledAt });
        row.HasIndex(job => new { job.SubjectType, job.SubjectId });
    }
}
