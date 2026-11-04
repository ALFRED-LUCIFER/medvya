Feature: Quick registration for a busy desk with a completion task
  As a receptionist, I want a quick registration mode, so that I can register a patient fast and complete the rest later.

  # chunk: Add quick registration with incomplete tag and auto-close
  @done
  Scenario: Incomplete tag after quick save
    When a patient is quick-registered
    Then the board card and patient header show an "Incomplete registration" tag until the task is closed

  @done
  Scenario: Completing missing fields closes the task
    Given a patient with an incomplete registration
    When the missing fields are completed later
    Then the task closes automatically

  @done
  Scenario: Duplicate check runs in quick mode
    When a patient is quick-registered
    Then the duplicate check (MDV-P1-016) still runs

  # chunk: Prove the 45-second quick registration
  @todo
  Scenario: Quick registration in under 45 seconds
    Given a trained user using keyboard only
    When they complete a quick registration
    Then it takes under 45 seconds (timed usability check)
