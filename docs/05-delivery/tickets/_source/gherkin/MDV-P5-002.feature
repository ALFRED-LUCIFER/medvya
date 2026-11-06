Feature: Migration: read-only source connection via self-hosted integration runtime (Oracle, SQL Server, MySQL, CSV/Excel) (N-04)
  As Medvya operations, I want a read-only outbound connection to a clinic's legacy data, so that migration is safe.

  # chunk: Add read-only outbound source connection
  @todo
  Scenario: Outbound only
    Given a clinic network
    When the connection is set up
    Then it requires only outbound connectivity with no inbound firewall changes
  @todo
  Scenario: Read-only user
    Given the source database user
    When a write is attempted
    Then the write fails

  # chunk: Add in-country staging and inventory
  @todo
  Scenario: Raw data staging
    Given extracted data
    When it lands
    Then it is in the in-country staging area, encrypted, isolated per tenant, with retention limits
  @todo
  Scenario: Connection test and inventory
    Given a registered connection
    When operations run the test
    Then tables and row counts are shown
