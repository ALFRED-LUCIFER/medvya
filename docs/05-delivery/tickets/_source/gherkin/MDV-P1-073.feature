Feature: Clinical advisor walks the six journeys and signs off (moved from MDV-P0-023)
  As a clinical advisor, I want to walk the six journeys and sign them off, so that the product matches real clinical work.

  # chunk: Walk the journeys and record sign-off
  @todo
  Scenario: Advisor sign-off is recorded
    Then sign-off, or a list of requested changes with owners, for each of J1-J6 is recorded in docs/02-design/journeys.md with a date

  @todo
  Scenario: Exit gate item is evidenced
    Then the P0 exit gate item "Clinical advisor walks all six journeys and signs off" is evidenced

  # chunk: Raise the requested changes as tickets
  @todo
  Scenario: Requested changes become tickets
    Then any change requested by the advisor is raised as a ticket in the relevant phase
    And changes to the pilot clinic flow are scheduled before the R1 pilot
