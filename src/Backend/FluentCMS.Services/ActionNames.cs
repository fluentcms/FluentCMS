namespace FluentCMS.Services;

public static class ActionNames
{
    #region Setup

    public const string SetupStarted = "SetupStarted";
    public const string SetupInitializeSite = "SetupInitializeSite";
    public const string SetupInitializePlugins = "SetupInitializePlugins";
    public const string SetupCompleted = "SetupCompleted";

    #endregion

    #region GlobalSettings

    public const string GlobalSettingsUpdated = "GlobalSettingsUpdated";

    #endregion

    #region Site

    public const string SiteCreated = "SiteCreated";
    public const string SiteUpdated = "SiteUpdated";
    public const string SiteDeleted = "SiteDeleted";

    #endregion

    #region Layout

    public const string LayoutCreated = "LayoutCreated";
    public const string LayoutUpdated = "LayoutUpdated";
    public const string LayoutDeleted = "LayoutDeleted";

    #endregion

    #region Page

    public const string PageCreated = "PageCreated";
    public const string PageUpdated = "PageUpdated";
    public const string PageDeleted = "PageDeleted";

    #endregion

    #region PluginDefinition

    public const string PluginDefinitionCreated = "PluginDefinitionCreated";
    public const string PluginDefinitionUpdated = "PluginDefinitionUpdated";
    public const string PluginDefinitionDeleted = "PluginDefinitionDeleted";

    #endregion


    #region Plugin

    public const string PluginCreated = "PluginCreated";
    public const string PluginUpdated = "PluginUpdated";
    public const string PluginDeleted = "PluginDeleted";

    #endregion

    #region ApiToken

    public const string ApiTokenCreated = "ApiTokenCreated";
    public const string ApiTokenUpdated = "ApiTokenUpdated";
    public const string ApiTokenSecretRegenerated = "ApiTokenSecretRegenerated";
    public const string ApiTokenDeleted = "ApiTokenDeleted";

    #endregion


    #region ContentType

    public const string ContentTypeCreated = "ContentTypeCreated";
    public const string ContentTypeUpdated = "ContentTypeUpdated";
    public const string ContentTypeDeleted = "ContentTypeDeleted";

    #endregion

    #region Content

    public const string ContentCreated = "ContentCreated";
    public const string ContentUpdated = "ContentUpdated";
    public const string ContentDeleted = "ContentDeleted";

    #endregion

    #region Role

    public const string RoleCreated = "RoleCreated";
    public const string RoleUpdated = "RoleUpdated";
    public const string RoleDeleted = "RoleDeleted";

    #endregion

    #region User

    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeleted = "UserDeleted";

    #endregion
}
