Feature: Platform analytics, practice health score and scheduled reports with small-count hiding
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Platform analytics, practice health score and scheduled reports with s
  @todo
  Scenario: Check 1
    Given a count is 4
    When the chart renders
    Then it shows as hidden
  @todo
  Scenario: Check 2
    Given a report
    When is scheduled
    Then only the named recipients get it and it holds no patient data
  @todo
  Scenario: Check 3
    Given a practice's health score falls
    When it is shown
    Then the reasons are listed
