Feature: Six end-to-end journeys as Playwright specs (X-03)
  As a developer, I want one Playwright spec per journey, so that each phase turns its journey steps green.

  # chunk: Add one Playwright spec per journey
  @done
  Scenario: Journey specs exist for J1 to J6
    Then tests/e2e contains one spec per journey with every storyboard step referencing screen IDs
    And P0 steps pass
    And later steps are fixme with phase tags

