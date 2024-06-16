namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldSettings
{
    private List<DateFormatOptions> FileTypes
    {
        get => [
            new("image", "Image"),
            new("audio", "Audio"),
            new("video", "Video"),
            new("other", "Other"),
        ];
    }
}
