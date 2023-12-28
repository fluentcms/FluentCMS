@RequiresFreshSetup
Feature: Basic User Client functionality

Background:
	Given I have a "UserClient"

Scenario: Create user
	Given I have Credentials
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created

Scenario: Get user
	Given I have Credentials
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I get a user with id
	Then user is returned

Scenario: Get all users
	Given I have Credentials
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I get all users
	Then all users are returned

Scenario: Update user
	Given I have Credentials
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I update a user
	Then user is updated
