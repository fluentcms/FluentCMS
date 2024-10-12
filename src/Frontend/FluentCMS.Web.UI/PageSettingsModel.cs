using System.ComponentModel.DataAnnotations;

public class PageSettingsModel {
        public Guid? Id { get; set; } = default!;
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Path { get; set; } = string.Empty;
        
        public Guid? ParentId { get; set; } = default;
        public Guid? LayoutId { get; set; } = default;
        public Guid? EditLayoutId { get; set; } = default;
        public Guid? DetailLayoutId { get; set; } = default;
        
        [Required]
        public int Order { get; set; } = default;
        
        [Required]
        public ICollection<Guid> ViewRoleIds { get; set; } = [];
        
        [Required]
        public ICollection<Guid> ContributorRoleIds { get; set; } = [];
        
        [Required]
        public ICollection<Guid> AdminRoleIds { get; set; } = [];
    }