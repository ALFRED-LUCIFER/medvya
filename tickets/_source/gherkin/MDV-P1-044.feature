Feature: Legal review of privacy notice, ToS, DPA and consent wording (DPDP India; UAE health data)
  As a founder, I want counsel to review legal texts, so that consent and notices are lawful before real patients register.

  # chunk: Obtain legal review and load approved texts
  @todo
  Scenario: Approved texts are versioned in the repo
    Then approved texts with version numbers (e.g. v2026.2) are stored in the repo
    And loaded into consent templates

  @todo
  Scenario: Review notes list consent model changes
    Then the review notes list any change to the consent model and the resulting ticket updates

  @todo
  Scenario: Sign-off precedes the first real patient
    Then sign-off is recorded before the first real patient is registered
