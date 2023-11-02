using FluentCMS.Repository;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Application.Sites;
public class SiteValidator : AbstractValidator<SiteDto>
{
    private readonly ISiteRepository _siteRepository;

    public SiteValidator(ISiteRepository siteRepository)
    {
        RuleFor(x => x.Name).NotEmpty().NotNull().WithSeverity(Severity.Info);
        RuleFor(x => x.Urls).MustAsync(BeUnique).WithSeverity(Severity.Info);
        _siteRepository = siteRepository;
    }

    private async Task<bool> BeUnique(SiteDto dto, List<string> list, CancellationToken token)
    {
        foreach (var url in list)
        {
            var site = await _siteRepository.GetByUrl(url);
            if (site != null && site.Id != dto.Id)
            {
                return false;
            }
        }
        return true;
    }
}
