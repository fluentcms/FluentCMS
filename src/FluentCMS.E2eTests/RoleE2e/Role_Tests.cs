using FluentCMS.E2eTests;
using FluentCMS.Repositories;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.E2eTests.RoleE2e;

public class Role_Tests : AuthorizedBaseE2eTest<IRoleClient, RoleClient>
{
    // GetAll
    [Fact]
    public async Task Should_GetAll_Role()
    {
        var site = await GetASite();
        var siteId = site.Id;

        // seed roles to db
        const int count = 10;
        var seededRoles = new List<Entities.Role>();
        for (int i = 0; i < count; i++)
        {
            seededRoles.Add(await SeedRole($"DummyRole-{i}", siteId));
        }

        // get all
        var roles = Client.GetAllAsync(siteId);

        // check roleIds match seeded roles
        roles.Result.Errors.ShouldBeEmpty();
        roles.Result.Data.ShouldNotBeNull();
        roles.Result.Data.ShouldNotBeEmpty();
        seededRoles.Select(x => x.Id).All(x => roles.Result.Data.Any(y => y.Id == x)).ShouldBeTrue();
    }

    // GetById
    [Fact]
    public async Task Should_GetById_Role()
    {
        var site = await GetASite();
        var siteId = site.Id;

        // seed a role
        var seededRole = await SeedRole(siteId: siteId);

        var role = await Client.GetByIdAsync(seededRole.Id);

        role.ShouldNotBeNull();
        role.Data.ShouldNotBeNull();
        role.Data.Name.ShouldBe(seededRole.Name);
        role.Data.Description.ShouldBe(seededRole.Description);
        role.Data.SiteId.ShouldBe(siteId);
    }

    // Create
    [Fact]
    public async Task Should_Create_Role()
    {
        var site = await GetASite();
        var siteId = site.Id;

        var role = new RoleCreateRequest()
        {
            SiteId = siteId,
            Name = "DummyRole",
            Description = "DummyRole Description"
        };

        var dbRole = await Client.CreateAsync(role);

        role.ShouldNotBeNull();
        role.Name.ShouldBe(role.Name);
        role.Description.ShouldBe(role.Description);
        role.SiteId.ShouldBe(siteId);
    }

    // Update
    [Fact]
    public async Task Should_Update_Role()
    {
        var site = await GetASite();
        var siteId = site.Id;

        //seed a role to be updated
        var roleToBeUpdated = await SeedRole(siteId: siteId);


        var updateRequest = new RoleUpdateRequest()
        {
            Id = roleToBeUpdated.Id,
            SiteId = siteId,
            Name = "UpdatedDummyRole",
            Description = "UpdatedDummyRole Description"
        };

        var updatedRole = await Client.UpdateAsync(updateRequest);

        roleToBeUpdated.ShouldNotBeNull();
        roleToBeUpdated.Id.ShouldBe(updateRequest.Id);
        roleToBeUpdated.Name.ShouldBe(updateRequest.Name);
        roleToBeUpdated.Description.ShouldBe(updateRequest.Description);
        roleToBeUpdated.SiteId.ShouldBe(siteId);
    }

    // Delete
    [Fact]
    public async Task Should_Delete_Role()
    {
        var site = await GetASite();
        var siteId = site.Id;

        // seed a role to be deleted
        var roleToBeDeleted = await SeedRole(siteId: siteId);

        var deletedRole = await Client.DeleteAsync(roleToBeDeleted.Id);

        deletedRole.ShouldNotBeNull();
        deletedRole.Data.ShouldBeTrue();

        // check from repository
        var repository = WebUi.Services.CreateScope().ServiceProvider.GetRequiredService<IRoleRepository>();

        var dbRole = await repository.GetById(roleToBeDeleted.Id);
        dbRole.ShouldBeNull();
    }

    private async Task<Web.UI.ApiClients.SiteResponse> GetASite()
    {
        await Login();
        var siteClient = WebUi.Services.CreateScope().ServiceProvider.GetRequiredService<SiteClient>();

        var sites = await siteClient.GetAllAsync();

        return sites.Data!.First();
    }

    private async Task<Entities.Role> SeedRole(string name = "DummyRole", Guid siteId = default)
    {
        var roleRepository = WebUi.Services.CreateScope().ServiceProvider.GetRequiredService<IRoleRepository>();
        var newRole = new Entities.Role()
        {
            Name = name,
            Description = $"{name} Description",
            SiteId = siteId
        };
        return (await roleRepository.Create(newRole))!;
    }
}
