Feature: Data-quality queue: problems grouped by type and fixed by rule; reversible (N-05)
  As Medvya operations, I want data-quality issues fixed by rule, so that migrated data is clean and changes are reversible.

  # chunk: Add fix-by-rule with undo
  @todo
  Scenario: Fix by rule
    Given a defined fix rule
    When applied
    Then it applies to all matching rows and records the rule, count and actor
  @todo
  Scenario: Undo restores originals
    Given an applied rule
    When undone
    Then original values are restored
  @todo
  Scenario: Remaining issues counted
    Given the dry-run report
    When viewed
    Then remaining issues are counted per type
