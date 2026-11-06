Feature: Six end-to-end journeys as Playwright specs and clinical advisor sign-off (X-03)
  As a clinical advisor, I want to walk the six journeys and sign them off, so that the product matches real clinical work.

  # chunk: Add one Playwright spec per journey
  @done
  Scenario: Journey specs exist for J1 to J6
    Then tests/e2e contains one spec per journey with every storyboard step referencing screen IDs
    And P0 steps pass
    And later steps are fixme with phase tags

  # chunk: Record advisor sign-off and raise change requests
  @todo
  Scenario: Advisor sign-off is recorded
    Then sign-off, or a list of requested changes with owners, for each of J1-J6 is recorded in docs/02-design/journeys.md with a date

  @todo
  Scenario: Requested changes become tickets
    Then any change requested by the advisor is raised as a ticket in the relevant phase before P1 scope freeze

  @todo
  Scenario: Exit gate item is evidenced
    Then the P0 exit gate item "Clinical advisor walks all six journeys and signs off" is evidenced
