using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Hosts;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;
using FluentCMS.Entities;

namespace FluentCMS.Api.Controllers;

public class HostController : BaseController
{
    private readonly IHostService _hostService;
    private readonly IMapper _mapper;

    public HostController(IHostService hostService, IMapper mapper)
    {
        _hostService = hostService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IApiResult<HostResponse>> Get(CancellationToken cancellationToken = default)
    {
        var host = await _hostService.Get(cancellationToken);
        var response = _mapper.Map<HostResponse>(host);
        return new ApiResult<HostResponse>(response);
    }

    [HttpPatch]
    public async Task<IApiResult<HostResponse>> Update(UpdateHostRequest request, CancellationToken cancellationToken = default)
    {
        var host = _mapper.Map<Host>(request);
        var updateHost = await _hostService.Update(host, cancellationToken);
        var hostResponse = _mapper.Map<HostResponse>(updateHost);
        return new ApiResult<HostResponse>(hostResponse);
    }
}
