Feature: Printable day sheet for the desk (no phone numbers or national IDs)
  As a receptionist, I want a paper day sheet, so that the desk can keep going if the internet is down.

  # chunk: Print the day sheet
  @todo
  Scenario: Print the day sheet
    Given a receptionist views today's schedule
    When they choose Print day sheet
    Then a one-page-per-doctor sheet is produced in the facility time zone
    And it excludes phone numbers and national IDs
    And an access-log entry records who printed it
