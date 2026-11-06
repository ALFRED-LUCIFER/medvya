Feature: App shell with role-based navigation (desktop sidebar, tablet rail, mobile tabs)
  As a staff member, I want navigation that shows only what my role may use, so that I find my work quickly.

  # chunk: Add role- and flag-filtered app shell navigation
  @done
  Scenario: Receptionist sees only Practice items
    Given I am a receptionist
    Then I see only Practice items (front desk, schedule, register)
    And I never see Clinical items

  @done
  Scenario: Doctor sees allowed items only
    Given I am a doctor
    Then I see Clinical and Practice items allowed by the matrix
    And items behind disabled flags are not rendered

  @done
  Scenario: Layout adapts to screen width
    Then at 768 px the layout switches to the rail
    And at 360-767 px it switches to bottom tabs

  @done
  Scenario: Keyboard navigation works
    Then a skip-to-content link, visible focus and logical tab order exist
    And the sidebar is a nav landmark

  @done
  Scenario: Navigation can be demonstrated per role
    Then the R0 capability "see role-based navigation" can be demonstrated for each role in the seeded tenant
