Feature: Waitlist with "offer earlier slot"
  As a receptionist, I want to offer freed slots to waitlisted patients, so that cancellations are refilled.

  # chunk: Add waitlist candidates and offers
  @todo
  Scenario: Freed slot shows matching candidates
    Given patients are on the waitlist
    When a slot frees
    Then matching candidates are shown ordered by request time

  @todo
  Scenario: Offer holds the slot and messages the patient
    When a receptionist clicks "Offer"
    Then a hold is placed and a message without clinical details is sent
    And if not accepted within the hold window, the next candidate can be offered

  @todo
  Scenario: Accepting books and removes from waitlist
    When the patient accepts
    Then the slot is booked
    And the patient is removed from the waitlist
