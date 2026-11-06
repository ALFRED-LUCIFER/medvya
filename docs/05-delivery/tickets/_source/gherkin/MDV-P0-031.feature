Feature: Accept ADR-012: PostgreSQL with EF Core, application-level national-ID encryption, hash-chained audit
  As founder, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Accept ADR-012: PostgreSQL with EF Core, application-level national-ID
  @todo
  Scenario: Check 1
    Given ADR-012
    When it is reviewed
    Then it is Accepted with date and decider and lists the alternatives rejected (Azure SQL, Cosmos DB, an ORM change)
  @todo
  Scenario: Check 2
    Given ADR-001, ADR-005 and ADR-006
    When they are read
    Then each points to ADR-012 for what changed
