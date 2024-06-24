﻿namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;
public interface IFieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool Required { get; set; }
    public bool Unique { get; set; }
    public string Description { get; set; }
    public int DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
    public string DataTableViewComponent { get; set; }
    public int FormViewOrder { get; set; }
    public int FormColWidth { get; set; }
    public string FormViewComponent { get; set; }
    public string Label { get; set; }
}

public interface IFieldModel<T> : IFieldModel
{
    public T DefaultValue { get; set; }
}

public abstract class FieldModel<T> : IFieldModel<T>
{
    public string Name { get; set; } = string.Empty;
    public abstract string Type { get; set; }
    public bool Required { get; set; }
    public bool Unique { get; set; }
    public string Description { get; set; } = string.Empty;
    public int DataTableColumnOrder { get; set; } = 0;
    public bool DataTableVisible { get; set; } = true;
    public string DataTableViewComponent { get; set; } = default!;
    public int FormViewOrder { get; set; } = 0;
    public int FormColWidth { get; set; } = 12;
    public string FormViewComponent { get; set; } = default!;
    public string Label { get; set; } = string.Empty;
    public T DefaultValue { get; set; } = default!;
}
