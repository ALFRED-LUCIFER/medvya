Feature: Encounters module: start a consultation from the board/Today, bound to one patient and appointment
  As a doctor, I want to start a consultation bound to one patient and appointment, so that every clinical screen is tied to the right person.

  # chunk: Add encounter creation, identity banner and board sync
  @todo
  Scenario: Start consultation creates one encounter
    Given an appointment with no encounter
    When the doctor starts the consultation twice
    Then exactly one active encounter exists for the appointment
    And the second start reuses it
  @todo
  Scenario: Identity banner on every clinical screen
    Given an open encounter for "Meera Test"
    When the doctor opens any P2 clinical screen
    Then the identity banner for that encounter is shown
    And switching patient requires closing or explicitly leaving the encounter
  @todo
  Scenario: Encounter events update the reception board
    Given the reception board is open
    When an encounter starts or ends
    Then the board reflects it within 2 seconds
  @todo
  Scenario: Encounter APIs require the care relationship policy
    Given a clinician with no care relationship or purpose
    When they call an encounter API
    Then the request is refused by the clinician relationship/purpose policy (MDV-P2-024)
