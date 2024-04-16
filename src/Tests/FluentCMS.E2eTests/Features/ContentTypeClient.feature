@RequiresFreshSetup
@RequiresAuthenticatedAdmin
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
		| field           | value                       |
		| slug            | dummy-field-slug            |
		| title           | dummy-field-title           |
		| description     | dummy-field-description     |
		| label           | dummy-field-label           |
		| placeholder     | dummy-field-placeholder     |
		| hint            | dummy-field-hint            |
		| defaultValue    | dummy-field-defaultValue    |
		| isRequired      | false                       |
		| isPrivate       | false                       |
		| fieldType       | dummy-field-fieldType       |
		| metadata.field1 | dummy-field-metadata-field1 |
		| metadata.field2 | dummy-field-metadata-field2 |
		| metadata.field3 | dummy-field-metadata-field3 |
		| metadata.field4 | dummy-field-metadata-field4 |

	Then I should see the field

@RequiresContentType
@RequiresContentTypeSetField
Scenario: Delete Field
	Given I have a ContentType
	When I delete a field
	Then Wait 1 second
	Then I should not see the field
