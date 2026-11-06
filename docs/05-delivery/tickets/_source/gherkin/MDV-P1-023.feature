Feature: Calendar views: day, week and list (per doctor and resource), mobile/tablet responsive
  As a receptionist, I want day, week and list calendar views, so that I can see and book appointments on any device.

  # chunk: Add day, week and list calendar views
  @done
  Scenario: Appointment states are distinguishable without colour
    Then states show an icon and a label and are announced to screen readers
    And empty slots are buttons with names like "Book Dr. Arjun Rao at 10:20"

  @done
  Scenario: Week and list match the day view
    Given the same appointments
    Then week and list views show the same appointments as the day view (consistency test)

  @done
  Scenario: Mobile defaults to the list view
    Given a viewport of 767 px or narrower
    Then the list/agenda view is the default with a pinned "+ Book" action

  @done
  Scenario: Calendar first render within budget in CI
    Given a 3-doctor day on a clinic PC over throttled 4G
    Then first render is under 2.5 s (Playwright budget)

  # chunk: Verify calendar budget on staging
  @todo
  Scenario: Calendar first render budget on staging under load
    Given the P1 load profile on staging
    Then the calendar first render is under 2.5 s
