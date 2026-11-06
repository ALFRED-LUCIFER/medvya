Feature: Licence verification console: reviewer queue, certificate view, approve or ask for changes
  As reviewer, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Licence verification console: reviewer queue, certificate view, approv
  @todo
  Scenario: Check 1
    Given a doctor is unverified
    When they try to sign a note
    Then the API refuses
  @todo
  Scenario: Check 2
    Given a reviewer approves
    When it is saved
    Then name, register and time are recorded
  @todo
  Scenario: Check 3
    Given a licence is rejected
    When the owner is told
    Then the reason is in plain words and other staff keep working
