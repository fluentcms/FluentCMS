using System.Text.Json;

namespace FluentCMS.Web.UI.Services.LocalStorage;

public class LocalStorageOptions
{
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions();
}
