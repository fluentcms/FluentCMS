namespace FluentCMS.Core.Content.Hooks;

// Orders hooks whose [HookOrder] is equal; hooks of equal rank keep their registration order. The plugin host's ranks a
// hook by the plugin order, so a plugin that another one depends on ranks lower (PluginRegistry).
public interface IHookTiebreaker
{
    int Rank(Type hookType);
}
