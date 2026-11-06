Feature: Data requests and offboarding runs from the console without opening records
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Data requests and offboarding runs from the console without opening re
  @todo
  Scenario: Check 1
    Given a closure is requested
    When it is approved
    Then a full export is delivered before any retention clock starts
  @todo
  Scenario: Check 2
    Given retention ends
    When the job runs
    Then records are deleted and a certificate is stored
  @todo
  Scenario: Check 3
    Given an operator
    When opens a request
    Then no record content is shown
