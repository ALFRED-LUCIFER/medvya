Feature: Queue TV display with tokens only, never names (P-05)
  As a patient in the waiting room, I want to see my token called on a TV, so that I know my turn without my name being shown.

  # chunk: Add queue TV display
  @done
  Scenario: TV payload has no patient identity
    Then the TV payload contains tokens, rooms, doctor display names and waits only
    And no patient names, MRNs or reasons (API contract test)

  @done
  Scenario: Display tokens are revocable and facility-scoped
    Then display tokens are revocable
    And each is scoped to one facility

  @done
  Scenario: Calling a patient updates the TV quickly
    When a patient is called
    Then the TV updates within 2 seconds
    And optionally plays a chime

  @done
  Scenario: Layout readable at 3 metres
    Then token text is at least 48 px
    And the layout works in Arabic and English
