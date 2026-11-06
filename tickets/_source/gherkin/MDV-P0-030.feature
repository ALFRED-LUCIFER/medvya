Feature: Shared Table component (shadcn) and table controller pattern; replace the four raw tables
  As a developer, I want one Table component, so that every list looks, behaves and is tested the same way.

  # chunk: Add the Table component and migrate the four screens
  @todo
  Scenario: Raw tables rejected
    Given a feature file
    When it contains a raw table, tr, td or th element
    Then lint fails
  @todo
  Scenario: Existing screens unchanged
    Given the four existing screens
    When they render
    Then they use the shared Table with keyboard, focus ring, axe and Arabic right-to-left intact
  @todo
  Scenario: Sort state in the controller
    Given a sortable column
    When a user sorts it
    Then aria-sort is set and the state lives in the controller hook
  @todo
  Scenario: Table states
    Given an empty, loading or error result
    When the table renders
    Then each state uses the shared pattern
