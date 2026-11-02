namespace FluentCMS.Core.Content.Hooks;

public enum HookKind
{
    BeforeValidate,
    BeforeCreate,
    AfterCreate,
    BeforeUpdate,
    AfterUpdate,
    BeforeDelete,
    AfterDelete,
    BeforePublish,
    AfterPublish,
    BeforeUnpublish,
    AfterUnpublish,
    BeforeRestore,
    AfterRestore,
}
