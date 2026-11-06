Feature: Make the Azure dev environment actually run on PostgreSQL: app settings, Entra token connection, migration runner in the deploy workflow, immutable ledger-digests container
  As developer, I want this delivered, so that the PostgreSQL move is safe to deploy.

  # chunk: Make the Azure dev environment actually run on PostgreSQL: app setting
  @todo
  Scenario: Check 1
    Given the Bicep app settings
    When the app starts in Azure
    Then it finds a database connection, the national-ID key id and the digest key and registers every module
  @todo
  Scenario: Check 2
    Given the deploy workflow
    When a tag is pushed
    Then migrations and roles run inside the private network and the deploy succeeds with no SQL firewall step
  @todo
  Scenario: Check 3
    Given the ledger-digests container
    When anyone with the app identity tries to delete or overwrite a digest
    Then the storage account refuses it
  @todo
  Scenario: Check 4
    Given a tenant chain that has had a digest
    When the digest is missing at verification
    Then a Critical alert is raised, not a warning
  @todo
  Scenario: Check 5
    Given the dev environment
    When the smoke test runs
    Then sign-in, patient create and chain verification all pass
