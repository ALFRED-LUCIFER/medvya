Feature: Family, emergency contact and guardian rules (under-18 guardian required)
  As a receptionist, I want guardian and emergency contact rules enforced, so that minors and contacts are handled safely.

  # chunk: Add family and emergency rules
  @done
  Scenario: Guardian required under 18
    Given a DOB indicating age under 18
    Then a guardian is required
    And save is blocked without one

  @done
  Scenario Outline: Emergency contact requirement by mode
    When a patient is registered in <mode> mode
    Then the emergency contact is <requirement>
    Examples:
      | mode  | requirement  |
      | full  | required     |
      | quick | not required |

  @done
  Scenario: Emergency contacts get no access
    When an emergency contact is saved
    Then no account is created
    And they never receive portal access or patient data

  @done
  Scenario: Relationship values are country-specific
    Given a UAE patient
    Then the relationship values include Sponsor
