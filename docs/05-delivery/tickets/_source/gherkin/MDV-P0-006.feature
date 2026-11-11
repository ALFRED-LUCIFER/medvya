Feature: Azure environments as code (Bicep): App Service, PostgreSQL private access, Blob, Key Vault, App Insights, Defender, budgets
  As a developer, I want the Azure environments defined as code, so that they are repeatable, private and cost-controlled.

  # chunk: Deploy environments from CI with what-if
  @todo
  Scenario: Dev environment deploys from CI without portal steps
    When CI runs "az deployment sub what-if" and a real deployment of dev
    Then both succeed with no manual portal steps

  # chunk: Lock down PostgreSQL, Key Vault and Storage
  @todo
  Scenario: PostgreSQL is private only
    Then the PostgreSQL server publicNetworkAccess is Disabled
    And a connectivity test from outside the VNet fails

  @todo
  Scenario: Key Vault and Storage are hardened
    Then Key Vault has purge protection enabled
    And Storage has allowBlobPublicAccess set to false

  # chunk: Add budgets, region policy and secret-free templates
  @todo
  Scenario: Each environment has a budget with alerts
    Then each environment has a budget with email alerts at 50, 80 and 100 percent

  @todo
  Scenario: Resources outside the country region are denied
    Then an Azure Policy assignment denies creating resources in any region other than the configured country region(s)

  @todo
  Scenario: Templates contain no secret values
    Then no secret value appears in any Bicep or parameter file
    And the PR description records the monthly private endpoint cost estimate
