@RequiresFreshSetup
@RequiresTestApp
Feature: Role Client
Background:
	Given I have a "RoleClient"

Scenario: Create Role
	Given I have a role
		| field       | value                 |
		| name        | DummyRole             |
		| description | DummyRole Description |
	When I Create Role
	Then Role Creation Result should be Success

Scenario: Update Role
	When I Update Role to
		| field       | value                         |
		| name        | UpdatedDummyRole              |
		| description | Updated DummyRole Description |
	Then Role Update Result should be Success

Scenario: Delete Role
	When I Delete Role
	Then Role Deletion Result should be Success

Scenario: GetAll Roles
	Given I have 10 Roles
	When I Get All Roles
	Then I should get 10 Roles
