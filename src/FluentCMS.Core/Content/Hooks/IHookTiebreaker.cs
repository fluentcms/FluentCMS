namespace FluentCMS.Core.Content.Hooks;

// Orders hooks whose [HookOrder] is equal. The plugin host replaces the default (no tiebreaker registered, so
// registration order decides) with the dependency order of the plugins: a plugin that another one depends on ranks lower.
public interface IHookTiebreaker
{
    int Rank(Type hookType);
}
