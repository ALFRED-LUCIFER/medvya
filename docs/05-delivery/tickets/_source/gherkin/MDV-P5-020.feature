Feature: External penetration test and remediation — no open high/critical findings
  As the founder, I want an external pen test remediated, so that no serious findings remain.

  # chunk: Complete pen test and remediation
  @todo
  Scenario: High and critical fixed
    Given the report
    When received
    Then all high and critical findings are fixed and retested (exit gate 3)
  @todo
  Scenario: Mediums tracked and report stored
    Given medium findings
    When reviewed
    Then they have dated remediation tickets
    And the report is stored in docs/compliance
