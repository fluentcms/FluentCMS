@RequiresFreshSetup
Feature: Account Client

    Background:
        Given I have a "SetupClient"
        Given I have an "AccountClient"

    Scenario: Authenticate with Valid Credentials
        Given I have Credentials
          | field    | value      |
          | username | superadmin |
          | password | Passw0rd!  |
        When I Authenticate
        Then Response Errors Should be Empty
        Then I Should Have a Token

    Scenario: Authenticate with Invalid Credentials
        Given I have Credentials
          | field    | value            |
          | username | superadmin       |
          | password | InvalidPassw0rd! |
        When I Authenticate
        Then Response Errors Should not be Empty

    Scenario: Register
        Given I have Credentials
          | field           | value               |
          | username        | DummyUser           |
          | password        | DummyPassw0rd!      |
          | confirmpassword | DummyPassw0rd!      |
          | email           | DummyUser@localhost |
        When I Register
        Then Response Errors Should be Empty

@RequiresRegisteredUser
Scenario: Register and Authenticate


@RequiresRegisteredUser
Scenario: Register and Authenticate and Change Password

