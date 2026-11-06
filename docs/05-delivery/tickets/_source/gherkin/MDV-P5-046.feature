Feature: Legal documents: versioned terms, privacy notice and DPA, re-acceptance gate that pauses booking only
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Legal documents: versioned terms, privacy notice and DPA, re-acceptanc
  @todo
  Scenario: Check 1
    Given a new version requires acceptance
    When an owner has not accepted
    Then online booking pauses and clinical work continues
  @todo
  Scenario: Check 2
    Given platform staff
    When try to edit wording
    Then the console offers no edit, only publish
  @todo
  Scenario: Check 3
    Given an owner accepts
    When it is saved
    Then user, version and time are recorded
