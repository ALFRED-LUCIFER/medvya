Feature: Privacy-safe global search (⌘K) with care-relationship check and "Request access"
  As a doctor or nurse, I want a fast, privacy-safe patient search, so that I find patients without exposing identities I am not allowed to see.

  # chunk: Add privacy-safe search results and keyboard use
  @todo
  Scenario: Results hide identifiers until opened
    Given a search for a patient
    When results are returned
    Then they contain only initials, age and MRN tail, never full names or identifiers (payload test)
  @todo
  Scenario: Keyboard operation
    Given the doctor is on any screen
    When they press Cmd/Ctrl+K, use arrow keys, Enter and Esc
    Then the overlay opens, navigates, opens the result and closes
    And screen-reader announcements are made
  @todo
  Scenario: Search latency
    Given typical search load
    When searches run
    Then p95 latency is under 300 ms

  # chunk: Add request access for patients outside the care relationship
  @todo
  Scenario: Open without relationship is blocked
    Given a patient outside the user's care relationship
    When the user tries to open the record
    Then it is blocked and "Request access" is offered
    And the request is audited and notifies the owner
