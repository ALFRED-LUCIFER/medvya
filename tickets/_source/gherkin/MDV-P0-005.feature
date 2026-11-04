Feature: Local development environment with Docker Compose (SQL Server, Azurite, mail and SMS sinks)
  As a developer, I want a one-command local environment, so that I can run and sign in to the full stack without real secrets.

  # chunk: Add one-command start and dev-only OIDC stub
  @done
  Scenario: Documented start command brings up the stack
    Given Docker is running on a clean machine
    When I run the documented start command
    Then the API, web/clinic and all dependencies start
    And the seeded synthetic tenant can sign in within 5 minutes

  @done
  Scenario: Dev OIDC stub refuses to start outside Development
    Given ASPNETCORE_ENVIRONMENT is not Development
    When the dev OIDC stub starts
    Then it refuses to start
    And a test verifies this

  # chunk: Keep local secrets out and add mail and SMS sinks
  @todo
  Scenario: No real secrets are needed locally
    Then no real secrets are needed to run locally
    And .env.example lists variable names only

  @todo
  Scenario: Locally sent email and SMS land in sinks
    When emails and SMS are sent locally
    Then they appear in the sinks
    And they are never delivered externally
