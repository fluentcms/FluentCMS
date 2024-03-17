@RequiresFreshSetup
@RequiresAuthenticatedAdmin
Feature: App Client

Background:
	Given I have an "AppClient"

Scenario: Create App
	Given I have App Create Request
		| field       | value |
		| slug        | test  |
		| title       | test  |
		| description | test  |
	When I Create App
	Then App Create result should be Success

@RequiresTestApp
Scenario: Update App
	Given I have App Update Request
		| field       | value |
		| slug        | test  |
		| title       | updated-test  |
		| description | updated-test  |
	When I Update App
	Then App Update result should be Success
	And App Update should match request

@RequiresTestApp
Scenario: Delete App
	When I Delete App
	Then App Delete result should be Success

Scenario: Get All Apps
	Given I have 10 Apps
	When I Get All Apps
	Then App GetAll result should be Success
	And App GetAll result should have 10 items

@RequiresTestApp
Scenario: Get App By Slug
	When I Get App By Slug "test"
	Then App GetBySlug result should be Success
