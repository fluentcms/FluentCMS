using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Mcp;

// What a site tells its agents. A row with no type is the guidelines of the site, which the handshake carries too, and a
// row with a type the instructions for that content type. A site has one row of each.
internal sealed class McpInstruction
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string? Type { get; set; }

    public string Text { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }
}

// A prompt an editor wrote for the agents of a site. Arguments is a JSON list of { name, description, required }, and
// Template names an argument as {{name}}.
internal sealed class McpPrompt
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string Arguments { get; set; } = "[]";

    public string Template { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }
}

internal sealed class McpModel : IPluginModelContributor
{
    public const string InstructionTable = "cms_mcp_instruction";
    public const string PromptTable = "cms_mcp_prompt";

    public const int TypeMaxLength = 64;
    public const int NameMaxLength = 64;
    public const int TitleMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpInstruction>(instruction =>
        {
            instruction.ToTable(InstructionTable);
            instruction.HasKey(item => item.Id);
            instruction.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            instruction.Property(item => item.SiteId).HasColumnName("site_id");
            instruction.Property(item => item.Type).HasColumnName("type").HasMaxLength(TypeMaxLength);
            instruction.Property(item => item.Text).HasColumnName("text").IsRequired();
            instruction.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            instruction.Property(item => item.UpdatedBy).HasColumnName("updated_by");
            instruction.HasIndex(item => item.SiteId).IsUnique().HasFilter("\"type\" IS NULL").HasDatabaseName("ux_cms_mcp_instruction_site");
            instruction.HasIndex(item => new { item.SiteId, item.Type }).IsUnique().HasFilter("\"type\" IS NOT NULL").HasDatabaseName("ux_cms_mcp_instruction_site_type");
        });

        modelBuilder.Entity<McpPrompt>(prompt =>
        {
            prompt.ToTable(PromptTable);
            prompt.HasKey(item => item.Id);
            prompt.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            prompt.Property(item => item.SiteId).HasColumnName("site_id");
            prompt.Property(item => item.Name).HasColumnName("name").HasMaxLength(NameMaxLength).IsRequired();
            prompt.Property(item => item.Title).HasColumnName("title").HasMaxLength(TitleMaxLength);
            prompt.Property(item => item.Description).HasColumnName("description").HasMaxLength(DescriptionMaxLength);
            prompt.Property(item => item.Arguments).HasColumnName("arguments").IsRequired();
            prompt.Property(item => item.Template).HasColumnName("template").IsRequired();
            prompt.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            prompt.Property(item => item.UpdatedBy).HasColumnName("updated_by");
            prompt.HasIndex(item => new { item.SiteId, item.Name }).IsUnique().HasDatabaseName("ux_cms_mcp_prompt_site_name");
        });
    }
}
