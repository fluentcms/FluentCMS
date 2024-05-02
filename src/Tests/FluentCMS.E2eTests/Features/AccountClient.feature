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

    Scenario: Register and Authenticate
        Given I have Credentials
          | field           | value               |
          | username        | DummyUser           |
          | password        | DummyPassw0rd!      |
          | confirmpassword | DummyPassw0rd!      |
          | email           | DummyUser@localhost |
        When I Register
        Then Response Errors Should be Empty
        Given I have Credentials
          | field    | value          |
          | username | DummyUser      |
          | password | DummyPassw0rd! |
        When I Authenticate
        Then Response Errors Should be Empty
        Then I Should Have a Token

    @RequiresAuthenticatedAdmin
    Scenario: Change Password
        Given I have a dto "UserChangePasswordRequest"
          | field       | value        |
          | OldPassword | Passw0rd!    |
          | NewPassword | NewPassw0rd! |
        When I ChangePassword
        Then Response Errors Should be Empty
        Given I have Credentials
          | field    | value        |
          | username | superadmin   |
          | password | NewPassw0rd! |
        When I Authenticate
        Then Response Errors Should be Empty
