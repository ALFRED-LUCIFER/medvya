Feature: Dark mode and high-contrast themes (X-02)
  As any user, I want dark and high-contrast themes, so that I can read comfortably.

  # chunk: Add dark and high-contrast themes
  @todo
  Scenario: Screens render correctly
    Given the core journeys
    When visual tests run in dark and high-contrast
    Then all screens render correctly
  @todo
  Scenario: Contrast meets AA
    Given both themes
    When the automated check runs
    Then contrast ratios meet AA
  @todo
  Scenario: Preference persists
    Given a user theme preference
    When they return
    Then it persists per user
