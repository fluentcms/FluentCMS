using FluentCMS.Content;

namespace FluentCMS.Core.Settings;

// A rule of one area that the attributes of its class cannot state, such as one that reads the schema. A write and an import
// run it on the document once its class has read and checked it, before anything is stored.
internal interface ISettingsCheck
{
    string Area { get; }

    IEnumerable<ValidationError> Check(object value);
}
