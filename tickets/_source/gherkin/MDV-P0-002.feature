Feature: Record and accept ADR-001 (stack) and supporting ADRs 003-006
  As a founder, I want the stack and supporting decisions formally accepted, so that the team builds on recorded, agreed decisions.

  # chunk: Accept ADR-001 and ADRs 003-006 with alternatives and conflict references
  @todo
  Scenario: ADRs are accepted with date and decider
    Given ADR-001, 003, 004, 005 and 006 are drafted
    When the founder reviews them
    Then each has status Accepted with a date and a decider

  @todo
  Scenario: Each ADR lists rejected alternatives
    Then each ADR lists the alternatives rejected and why
    And the alternatives include NestJS, Next.js, PostgreSQL, Cosmos DB and AKS in P0-P6

  @todo
  Scenario: Source-conflict rows reference the accepted ADRs
    Then rows C-01 to C-07 in docs/01-prd/source-conflicts.md reference the accepted ADRs

  @todo
  Scenario: Design project owner is told to update the superseded pages
    When the ADRs are accepted
    Then a short note is sent to the design project owner to update the Build Plan and Azure Architecture pages
