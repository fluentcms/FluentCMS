Feature: Basic User Client functionality

Background:
	Given Reset Setup
	Given Setup is initialized

Scenario: Create user
	Given Dummy Data for User Creation
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created

Scenario: Get user
	Given Dummy Data for User Creation
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I get a user with id
	Then user is returned

Scenario: Get all users
	Given Dummy Data for User Creation
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I get all users
	Then all users are returned

Scenario: Update user
	Given Dummy Data for User Creation
		| field    | value               |
		| username | DummyUser           |
		| email    | DummyUser@localhost |
		| password | DummyPassw0rd!      |
	When I create a user
	Then user is created
	When I update a user
	Then user is updated

# We won't have "Delete user" functionality in the first release
