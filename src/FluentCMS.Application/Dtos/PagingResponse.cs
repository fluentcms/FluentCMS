namespace FluentCMS.Application.Dtos;
public class PagingResponse<TData>
{
    public IEnumerable<TData> Data { get; set; } = new List<TData>();
    public long Total { get; set; }
}
