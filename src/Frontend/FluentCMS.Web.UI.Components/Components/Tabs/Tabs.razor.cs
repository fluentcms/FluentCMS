namespace FluentCMS.Web.UI.Components;

public partial class Tabs
{
	[Parameter]
	public string Active { get; set; }

	[Parameter]
	public EventCallback<string> OnChange { get; set; }

	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;

	public string Value { get; set; }

	protected override async Task OnInitializedAsync()
	{
		Value = Active;
	}

	public async Task Activate(string name)
	{
		Value = name;
		StateHasChanged();
		await OnChange.InvokeAsync(Value);
	}
}
