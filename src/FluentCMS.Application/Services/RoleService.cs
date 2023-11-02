using Ardalis.GuardClauses;
using FluentCMS.Application.Dtos;
using FluentCMS.Application.Dtos.Users;
using FluentCMS.Entities.Users;
using FluentCMS.Repository;

namespace FluentCMS.Application.Services;

public interface IRoleService
{
    Task<RoleDto> GetById(Guid id);
    Task<PagingResponse<RoleDto>> Search(SearchRoleRequest request);
    Task<Guid> Create(CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task Edit(EditRoleRequest request, CancellationToken cancellationToken = default);
    Task Delete(DeleteRoleRequest request, CancellationToken cancellationToken = default);
}

internal class RoleService(AutoMapper.IMapper mapper, IGenericRepository<Role> roleRepository) : IRoleService
{
    public async Task<RoleDto> GetById(Guid id)
    {
        var roles = await roleRepository.GetById(id)
            ?? throw new ApplicationException("Requested role does not exists.");
        return mapper.Map<RoleDto>(roles);
    }

    public async Task<PagingResponse<RoleDto>> Search(SearchRoleRequest request)
    {
        var roles = await roleRepository.GetAll();
        return new PagingResponse<RoleDto>
        {
            Data = roles.Select(x => mapper.Map<RoleDto>(x)),
            Total = roles.Count(),
        };
    }

    public async Task<Guid> Create(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.Name);

        var role = new Role
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = request.Name,
            Description = request.Description,
            AutoAssigned = request.AutoAssigned,
            SiteId = request.SiteId,
        };
        await roleRepository.Create(role, cancellationToken);
        return role.Id;
    }

    public async Task Edit(EditRoleRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.Id);
        Guard.Against.NullOrWhiteSpace(request.Name);

        var role = await roleRepository.GetById(request.Id);
        if (role == null)
            throw new ApplicationException("Provided RoleId does not exists.");

        role.LastUpdatedAt = DateTime.UtcNow;
        role.LastUpdatedBy = "";
        role.Name = request.Name;
        role.Description = request.Description;
        role.AutoAssigned = request.AutoAssigned;
        role.SiteId = request.SiteId;
        await roleRepository.Update(role, cancellationToken);
    }

    public async Task Delete(DeleteRoleRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.Id);

        var role = await roleRepository.GetById(request.Id);
        if (role == null)
            throw new ApplicationException("Provided RoleId does not exists.");

        await roleRepository.Delete(role.Id, cancellationToken);
    }
}
