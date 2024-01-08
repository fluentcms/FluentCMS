@RequiresFreshSetup
@RequiresTestApp
Feature: Content Type Client

Background:
	Given I have a "ContentTypeClient"

Scenario: Get All
	Given I have 10 ContentTypes
	When I get all content types
	Then I should see 10 content types

Scenario: Create
	Given I have a ContentTypeCreateRequest
		| field       | value            |
		| title       | test             |
		| description | test description |
		| slug        | test-slug        |
	When I create a content type
	Then I should see the content type

@RequiresContentType
Scenario: Update
	Given I have a ContentTypeUpdateRequest
		| field       | value                    |
		| title       | test-updated             |
		| description | test updated description |
	When I update a content type
	Then I should see the updated content type

@RequiresContentType
Scenario: Delete
	When I delete a content type
	Then I should not see the content type

@RequiresContentType
Scenario: Set Field
	Given I have a ContentType
	When I set a field
	Then I should see the field

@RequiresContentType
@RequiresContentTypeSetField
Scenario: Delete Field
	Given I have a ContentType
	When I delete a field
	Then I should not see the field
