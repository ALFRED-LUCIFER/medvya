Feature: Safe retries: idempotency keys, request timeouts and bounded retry
  As a receptionist, I want a dropped connection never to double-book or double-register, so that I can retry without fear.

  # chunk: Add idempotent booking, check-in and registration requests
  @todo
  Scenario: Network drops after a booking request is sent
    Given the receptionist submits a booking and the response never arrives
    When the client retries with the same Idempotency-Key
    Then exactly one appointment exists
    And the receptionist sees the confirmed booking

  @todo
  Scenario: Slot taken meanwhile
    Given a retry arrives after another user took the slot
    Then the API returns a conflict and the UI offers other slots
    And no duplicate appointment is created

  @todo
  Scenario: Check-in and registration are idempotent too
    Given a check-in or a registration request repeats with the same key
    Then one visit or one patient exists and the first outcome is returned

  @todo
  Scenario: Keys are scoped and hold no patient data
    Then a key is scoped to tenant and user, stores no patient data and expires

  # chunk: Add the shared API client with timeouts and bounded retry
  @todo
  Scenario: Request times out
    Given the API does not answer within 15 seconds
    Then the UI shows a retry message without clearing the form

  @todo
  Scenario: Retries are bounded and keep the key
    When a request is retried
    Then retries are limited, jittered and reuse the same Idempotency-Key
