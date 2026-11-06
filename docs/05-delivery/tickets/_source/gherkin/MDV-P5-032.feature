Feature: Mobile foundation: `mobile/` Expo workspace, shared tokens and i18n, RTL, CI and EAS builds
  As developer, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Mobile foundation: `mobile/` Expo workspace, shared tokens and i18n, R
  @todo
  Scenario: Check 1
    Given the workspace
    When CI runs
    Then both apps type-check, lint, test and produce an EAS preview build
  @todo
  Scenario: Check 2
    Given Arabic is selected
    When the app restarts
    Then layout is right-to-left while dates and phone numbers stay left-to-right
  @todo
  Scenario: Check 3
    Given a string is hard-coded in a component
    When lint runs
    Then the build fails
  @todo
  Scenario: Check 4
    Given the tokens file changes
    When web and native build
    Then both pick up the same colours and spacing
