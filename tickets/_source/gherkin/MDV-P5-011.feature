Feature: Emirates ID card-reader check-in under 30 seconds and UAE PASS (N-06)
  As a receptionist, I want to check in by Emirates ID reader, so that check-in is fast.

  # chunk: Choose and integrate the card reader
  @todo
  Scenario: Reader chosen
    Given the card-reader dependency
    When decided
    Then the hardware model is chosen and sourced with a supported integration path (local agent or WebUSB)

  # chunk: Add reader check-in under 30 seconds
  @todo
  Scenario: Median under 30 s
    Given a timed test
    When check-in uses the reader
    Then median check-in is under 30 s
  @todo
  Scenario: Expired EID warns
    Given an expired EID
    When read
    Then a warning shows but check-in is not blocked
  @todo
  Scenario: Confirm before save
    Given data read from the card
    When shown
    Then staff confirm it before save
