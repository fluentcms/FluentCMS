namespace FluentCMS.Admin.Components;

/// <summary>One key of the sort of a <see cref="CmsDataGrid{TItem}"/>: the name of a column and its direction.</summary>
/// <param name="Column">The <see cref="CmsDataGridColumn{TItem}.Name"/> of the column.</param>
/// <param name="Descending">Whether the key sorts from the largest value.</param>
public sealed record CmsSortKey(string Column, bool Descending = false);
