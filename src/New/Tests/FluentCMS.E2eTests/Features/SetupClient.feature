Feature: Basic functionality Test of Host API client

Background:
	Given I have a SetupClient
	Then Reset Setup

Scenario: Get Setup Status
	When I get setup IsInitialized 
	Then Setup initialization status should be "false"
	When I Start the host
	# if i get result immidiately i get a wrong result i guess this is expecte behaviour
	Then Wait a Bit 2secs
	When I get setup IsInitialized
	Then Setup initialization status should be "true"
