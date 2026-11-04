Feature: Duplicate warning while typing and duplicate resolution before save
  As a receptionist, I want to be warned about possible duplicates, so that I do not create a second record for the same person.

  # chunk: Add duplicate detection and resolution before save
  @done
  Scenario: Candidate suggested by name and DOB or exact identifier
    Given a similar name with identical DOB, or an exact ABHA, EID or passport match
    Then a candidate is suggested
    And a phone-only match never produces a suggestion

  @done
  Scenario: Responses expose only masked data
    When a duplicate check returns candidates
    Then only initials, age and masked identifiers are shown
    And full details appear only after "Use existing record" is chosen

  @done
  Scenario: Save is blocked until the duplicate is resolved
    Given a possible duplicate
    Then save stays disabled until the receptionist resolves it
    And the decision and actor are audited

  @done
  Scenario Outline: Resolution actions
    When the receptionist chooses <action>
    Then <result>
    Examples:
      | action                | result                                          |
      | Use existing record   | that patient opens and no new one is created     |
      | Request merge review  | a merge request is created (MDV-P1-017)         |

  @done
  Scenario: Duplicate check latency
    Given 50,000 synthetic patients
    Then duplicate-check p95 latency is under 300 ms
