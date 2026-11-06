Feature: Voice appointment assistant v1 (inbound): book/move/cancel from the live calendar, identity check, no medical advice, staff handover (N-10)
  As a caller, I want an automated assistant to book, move or cancel my appointment, so that I can do it after hours.

  # chunk: Add identity check before any detail
  @todo
  Scenario: No detail before identity
    Given a caller
    When identity verification has not succeeded
    Then no appointment detail is disclosed

  # chunk: Add booking via shared service
  @todo
  Scenario: Same rules as reception
    Given a voice booking
    When made
    Then it obeys the same concurrency and audit rules as reception

  # chunk: Add safety refusals and emergency transfer
  @todo
  Scenario: No medical advice
    Given red-team call scripts
    When run
    Then the assistant never gives medical advice, triage or results
  @todo
  Scenario: Emergency transfer
    Given emergency phrases in each tested language
    When spoken
    Then the emergency number script plays and the call is transferred immediately

  # chunk: Add owner switches and consent wording
  @todo
  Scenario: Owner controls
    Given owner settings
    When changed
    Then the assistant can be switched on or off per branch and hour
    And call recording follows the configured consent wording
