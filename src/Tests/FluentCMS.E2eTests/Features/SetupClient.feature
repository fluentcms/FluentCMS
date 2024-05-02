Feature: Host API client

    Background:
        Given I have a "SetupClient"
        When I Reset Setup

# Reset Setup

    Scenario: Reset Setup
        When I Reset Setup
        When I Fetch Setup IsInitialized
        Then Setup initialization status should be false

# Start Setup

    @RequiresResetSetup
    Scenario: Start Setup
        When I Start Setup
          | field            | value                |
          | username         | superadmin           |
          | email            | superadmin@localhost |
          | password         | Passw0rd!            |
          | AppTemplateName  | Blank                |
          | SiteTemplateName | Blank                |
          | AdminDomain      | localhost:5000       |
        Then Wait 2 seconds
        When I Fetch Setup IsInitialized
        Then Setup initialization status should be true

# Reinitializing should throw Error

    @RequiresResetSetup
    Scenario: Reinitializing should throw Error
        When I Start Setup
          | field            | value                |
          | username         | superadmin           |
          | email            | superadmin@localhost |
          | password         | Passw0rd!            |
          | AppTemplateName  | Blank                |
          | SiteTemplateName | Blank                |
          | AdminDomain      | localhost:5000       |
        Then Wait 2 seconds
        When I Start Setup again should throw Error

# Test RequiresResetSetup decorator

    @RequiresResetSetup
    Scenario: Test RequiresResetSetup decorator
        When I Fetch Setup IsInitialized
        Then Setup initialization status should be false
